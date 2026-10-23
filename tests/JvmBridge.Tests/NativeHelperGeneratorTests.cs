using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;

using JvmBridge.Agents;
using JvmBridge.ClassFiles;
using JvmBridge.Generator;
using JvmBridge.Native;
using JvmBridge.Runtime;
using JvmBridge.Tests.ClassFiles;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>Verifies the Java class and unmanaged entry points generated for native helper classes.</summary>
public sealed unsafe class NativeHelperGeneratorTests
{
    private const string Helpers = """
        using System;
        using System.Linq;
        using JvmBridge.Agents;
        using JvmBridge.Runtime;

        namespace Sample;

        [JavaClass("sample/Callbacks")]
        public static partial class Callbacks
        {
            public static int Pings;

            [JavaMethod]
            public static int Twice(int value) => value * 2;

            [JavaMethod("combine")]
            public static long Combine(long left, int right, bool negate, char marker) => (negate ? -(left + right) : left + right) + marker;

            [JavaMethod]
            public static void Ping(JavaEnvironment environment) => Pings += environment is null ? 100 : 1;

            [JavaMethod]
            public static int Fails() => throw new InvalidOperationException("contained");

            [JavaMethod]
            public static nint Echo([JavaType("Ljava/util/List;")] nint value) => value;

            [JavaMethod]
            public static int Sum(int[] values) => values is null ? -1 : values.Sum();

            [JavaMethod]
            public static byte[] Bytes(string text) => null;

            [JavaMethod]
            public static string[] Names(bool[] flags, char[] letters, long[] numbers, double[] ratios) => null;

            [JavaMethod]
            public static int Reject(int value) => value < 0 ? throw new JavaThrowException("java/lang/IllegalArgumentException", "negative") : value;
        }

        public static class Probe
        {
            public static JavaClassDefinition Definition() => Callbacks.Definition;

            public static int Pings() => Callbacks.Pings;
        }
        """;

    private static readonly List<RegisteredNative> Registered = [];

    private static readonly List<string> Thrown = [];

    private static string s_pendingClass = string.Empty;

    /// <summary>The generated Java class declares each method as public static native with the descriptor derived from the C# signature.</summary>
    [Fact]
    public void DeclaresNativeMethodsWithDerivedDescriptors()
    {
        JavaClassFile file = JavaClassFile.Read(Load(Helpers).Definition().Bytes.Span);

        Assert.Equal(expected: "sample/Callbacks", file.Name);
        Assert.Equal(expected: "(I)I", file.GetMethod(name: "twice").Descriptor);
        Assert.Equal(expected: "(JIZC)J", file.GetMethod(name: "combine").Descriptor);
        Assert.Equal(expected: "()V", file.GetMethod(name: "ping").Descriptor);
        Assert.Equal(expected: "(Ljava/util/List;)Ljava/lang/Object;", file.GetMethod(name: "echo").Descriptor);
        Assert.All(file.Methods, static method => Assert.Equal(ClassFileWriter.PublicStaticNative, method.AccessFlags));
    }

    /// <summary>The helper class gets the class file version of JavaRelease, so that a Java 6 or 7 JVM loads it: 50 for 6, 51 for 7, and 52 for 8 or when JavaRelease is missing.</summary>
    [Fact]
    public void HelperClassesFollowJavaRelease()
    {
        string defaulted = Run(Helpers).GeneratedTrees.Single().ToString();
        string java6 = Run(Helpers, javaRelease: "6").GeneratedTrees.Single().ToString();
        string java7 = Run(Helpers, javaRelease: "7").GeneratedTrees.Single().ToString();
        string java17 = Run(Helpers, javaRelease: "17").GeneratedTrees.Single().ToString();

        Assert.Contains(expectedSubstring: "majorVersion: 52)", defaulted, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "majorVersion: 50)", java6, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "majorVersion: 51)", java7, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "majorVersion: 61)", java17, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "JVMBRIDGE_CLASS_VERSION", defaulted, StringComparison.Ordinal);
    }

    /// <summary>Primitive and string arrays map to Java array descriptors.</summary>
    [Fact]
    public void MapsArraysToJavaArrayDescriptors()
    {
        JavaClassFile file = JavaClassFile.Read(Load(Helpers).Definition().Bytes.Span);

        Assert.Equal(expected: "([I)I", file.GetMethod(name: "sum").Descriptor);
        Assert.Equal(expected: "(Ljava/lang/String;)[B", file.GetMethod(name: "bytes").Descriptor);
        Assert.Equal(expected: "([Z[C[J[D)[Ljava/lang/String;", file.GetMethod(name: "names").Descriptor);
    }

    /// <summary>An array argument is copied into a managed array, a null array is passed as null, and a null array result is returned as null without touching the JVM.</summary>
    [Fact]
    public void MarshalsArraysAndNulls()
    {
        Registered.Clear();
        ProbeAccess probe = Load(Helpers);
        JNINativeInterface_ table = new() { RegisterNatives = &Register, ExceptionCheck = &NoException, GetArrayLength = &ThreeElements, GetIntArrayRegion = &FillOneTwoThree };
        JNINativeInterface_* tablePointer = &table;
        JNINativeInterface_** environment = &tablePointer;

        using JavaEnvironment javaEnvironment = new((nint)environment);

        probe.Definition().RegisterNatives?.Invoke(javaEnvironment, arg2: 1);

        delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, int> sum = (delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, int>)Find(name: "sum");
        delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, _jobject*> bytes = (delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, _jobject*>)Find(name: "bytes");

        _jobject* type = null;
        _jobject* missing = null;
        _jobject* array = (_jobject*)5;
        _jobject* nullResult = bytes(environment, type, missing);

        Assert.Equal(expected: 6, sum(environment, type, array));
        Assert.Equal(expected: -1, sum(environment, type, missing));
        Assert.Null(nullResult);
    }

    /// <summary>The default Java name is the C# name with a lower-case first letter, and an explicit name wins.</summary>
    [Fact]
    public void NamesJavaMethodsInJavaStyle()
    {
        string[] names = [.. JavaClassFile.Read(Load(Helpers).Definition().Bytes.Span).Methods.Select(static method => method.Name)];

        Assert.Equal(["twice", "combine", "ping", "fails", "echo", "sum", "bytes", "names", "reject"], names);
    }

    /// <summary>A C# method that throws JavaThrowException makes the entry point raise that Java exception and return a default value.</summary>
    [Fact]
    public void RaisesJavaExceptions()
    {
        Registered.Clear();
        Thrown.Clear();
        ProbeAccess probe = Load(Helpers);

        JNINativeInterface_ table = new()
        {
            RegisterNatives = &Register,
            ExceptionCheck = &NoException,
            FindClass = &FindAnyClass,
            DeleteLocalRef = &DeleteReference,
            ThrowNew = &RecordThrow
        };

        JNINativeInterface_* tablePointer = &table;
        JNINativeInterface_** environment = &tablePointer;

        using JavaEnvironment javaEnvironment = new((nint)environment);

        probe.Definition().RegisterNatives?.Invoke(javaEnvironment, arg2: 1);

        delegate* unmanaged<JNINativeInterface_**, _jobject*, int, int> reject = (delegate* unmanaged<JNINativeInterface_**, _jobject*, int, int>)Find(name: "reject");

        _jobject* type = null;
        int accepted = 4;
        int refused = -1;

        Assert.Equal(expected: 4, reject(environment, type, accepted));
        Assert.Empty(Thrown);
        Assert.Equal(expected: 0, reject(environment, type, refused));
        Assert.Equal(["java/lang/IllegalArgumentException: negative"], Thrown);
    }

    /// <summary>Registration hands the JVM entry points that marshal arguments, call the C# method, and contain its failures.</summary>
    [Fact]
    public void RegistersEntryPointsThatMarshalAndContainFailures()
    {
        Registered.Clear();
        ProbeAccess probe = Load(Helpers);
        JNINativeInterface_ table = new() { RegisterNatives = &Register, ExceptionCheck = &NoException };
        JNINativeInterface_* tablePointer = &table;
        JNINativeInterface_** environment = &tablePointer;

        using JavaEnvironment javaEnvironment = new((nint)environment);

        probe.Definition().RegisterNatives?.Invoke(javaEnvironment, arg2: 1);

        Assert.Equal(
            ["twice", "combine", "ping", "fails", "echo", "sum", "bytes", "names", "reject"],
            Registered.Select(static native => native.Name)
        );
        Assert.Equal(expected: 42, CallTwice(environment, value: 21));
        Assert.Equal(45L + 'A', CallCombine(environment, left: 40L, right: 5, negate: false));
        Assert.Equal(-45L + 'A', CallCombine(environment, left: 40L, right: 5, negate: true));
        CallPing(environment);
        Assert.Equal(expected: 1, probe.Pings());
        Assert.Equal(expected: 0, CallFails(environment));
        Assert.Equal(expected: 7, CallEcho(environment, value: 7));
        Assert.Equal(expected: "(Ljava/util/List;)Ljava/lang/Object;", Registered.Single(static native => native.Name == "echo").Signature);
    }

    /// <summary>Invalid helper classes and methods are reported with their own diagnostics.</summary>
    [Theory]
    [InlineData("[JavaClass(\"a/B\")] public partial class Bad { [JavaMethod] public static void M() { } }", "JVMB002")]
    [InlineData("[JavaClass(\"a/B\")] public static class Bad { [JavaMethod] public static void M() { } }", "JVMB002")]
    [InlineData("[JavaClass(\"a.B\")] public static partial class Bad { [JavaMethod] public static void M() { } }", "JVMB002")]
    [InlineData("[JavaClass(\"a/B\")] public static partial class Bad { public static void M() { } }", "JVMB002")]
    [InlineData("[JavaClass(\"a/B\")] public static partial class Bad { [JavaMethod] public static void M(object value) { } }", "JVMB003")]
    [InlineData("[JavaClass(\"a/B\")] public static partial class Bad { [JavaMethod] public static void M(ref int value) { } }", "JVMB003")]
    [InlineData("[JavaClass(\"a/B\")] public static partial class Bad { [JavaMethod] public static List<int> M() => null; }", "JVMB003")]
    [InlineData("[JavaClass(\"a/B\")] public static partial class Bad { [JavaMethod] public static void M(object[] values) { } }", "JVMB003")]
    [InlineData("[JavaClass(\"a/B\")] public static partial class Bad { [JavaMethod] public static void M(int[][] values) { } }", "JVMB003")]
    [InlineData("[JavaClass(\"a/B\")] public static partial class Bad { [JavaMethod] public static void M(int[,] values) { } }", "JVMB003")]
    public void ReportsInvalidDeclarations(string declaration, string expected)
    {
        Diagnostic[] diagnostics = [.. Run("using System.Collections.Generic; using JvmBridge.Agents; " + declaration).Diagnostics];

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == expected);
    }

    private static int CallCombine(JNINativeInterface_** environment, long left, int right, bool negate)
    {
        _jobject* type = null;
        byte flag = negate ? (byte)1 : (byte)0;
        ushort marker = 'A';
        delegate* unmanaged<JNINativeInterface_**, _jobject*, long, int, byte, ushort, long> function = (delegate* unmanaged<JNINativeInterface_**, _jobject*, long, int, byte, ushort, long>)Find(name: "combine");

        return (int)function(environment, type, left, right, flag, marker);
    }

    private static nint CallEcho(JNINativeInterface_** environment, nint value)
    {
        _jobject* type = null;
        delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, _jobject*> function = (delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, _jobject*>)Find(name: "echo");

        return (nint)function(environment, type, (_jobject*)value);
    }

    private static int CallFails(JNINativeInterface_** environment)
    {
        _jobject* type = null;
        delegate* unmanaged<JNINativeInterface_**, _jobject*, int> function = (delegate* unmanaged<JNINativeInterface_**, _jobject*, int>)Find(name: "fails");

        return function(environment, type);
    }

    private static void CallPing(JNINativeInterface_** environment)
    {
        _jobject* type = null;
        delegate* unmanaged<JNINativeInterface_**, _jobject*, void> function = (delegate* unmanaged<JNINativeInterface_**, _jobject*, void>)Find(name: "ping");

        function(environment, type);
    }

    private static int CallTwice(JNINativeInterface_** environment, int value)
    {
        _jobject* type = null;
        delegate* unmanaged<JNINativeInterface_**, _jobject*, int, int> function = (delegate* unmanaged<JNINativeInterface_**, _jobject*, int, int>)Find(name: "twice");

        return function(environment, type, value);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(string).Assembly.Location) ?? throw new InvalidOperationException(message: "Runtime assembly directory unavailable.");
        List<MetadataReference> references = [.. Directory.EnumerateFiles(runtimeDirectory, searchPattern: "*.dll").Select(static path => MetadataReference.CreateFromFile(path))];
        references.Add(MetadataReference.CreateFromFile(typeof(JavaAgent).Assembly.Location));

        return CSharpCompilation.Create(
            "NativeHelpers" + Guid.NewGuid().ToString(format: "N"),
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
        );
    }

    [UnmanagedCallersOnly]
    private static void DeleteReference(JNINativeInterface_** environment, _jobject* reference)
    {
    }

    [UnmanagedCallersOnly]
    private static void FillOneTwoThree(JNINativeInterface_** environment, _jobject* array, int start, int length, int* buffer)
    {
        for (int index = 0; index < length; index++)
            buffer[index] = index + 1;
    }

    private static nint Find(string name)
    {
        return Registered.Single(native => native.Name == name).Function;
    }

    [UnmanagedCallersOnly]
    private static _jobject* FindAnyClass(JNINativeInterface_** environment, byte* name)
    {
        s_pendingClass = Marshal.PtrToStringUTF8((nint)name) ?? string.Empty;

        return (_jobject*)0x1234;
    }

    private static ProbeAccess Load(string source)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new NativeHelperGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(CreateCompilation(source), out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        Diagnostic[] errors = [.. generatorDiagnostics, .. output.GetDiagnostics()];
        errors = [.. errors.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        SyntaxTree[] trees = [.. driver.GetRunResult().GeneratedTrees];
        SyntaxTree generated = Assert.Single(trees);

        Assert.Empty(errors);
        Assert.Contains(expectedSubstring: "UnmanagedCallersOnly", generated.ToString(), StringComparison.Ordinal);

        using MemoryStream stream = new();

        Assert.True(output.Emit(stream).Success);
        stream.Position = 0;
        Assembly assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(stream);
        Type probe = assembly.GetType(name: "Sample.Probe", throwOnError: true) ?? throw new InvalidOperationException(message: "Probe was not generated.");

        return new ProbeAccess(
            probe.GetMethod(name: "Definition")?.CreateDelegate<Func<JavaClassDefinition>>() ?? throw new InvalidOperationException(message: "Probe.Definition is missing."),
            probe.GetMethod(name: "Pings")?.CreateDelegate<Func<int>>() ?? throw new InvalidOperationException(message: "Probe.Pings is missing.")
        );
    }

    [UnmanagedCallersOnly]
    private static byte NoException(JNINativeInterface_** environment)
    {
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int RecordThrow(JNINativeInterface_** environment, _jobject* type, byte* message)
    {
        Thrown.Add(s_pendingClass + ": " + Marshal.PtrToStringUTF8((nint)message));

        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Register(JNINativeInterface_** environment, _jobject* type, JNINativeMethod* methods, int count)
    {
        for (int index = 0; index < count; index++)
        {
            string name = Marshal.PtrToStringUTF8((nint)methods[index].name) ?? string.Empty;
            string signature = Marshal.PtrToStringUTF8((nint)methods[index].signature) ?? string.Empty;
            Registered.Add(new RegisteredNative(name, signature, (nint)methods[index].fnPtr));
        }

        return 0;
    }

    private static GeneratorDriverRunResult Run(string source, string? javaRelease = null)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new NativeHelperGenerator().AsSourceGenerator()], optionsProvider: new ReleaseOptionsProvider(javaRelease));
        driver = driver.RunGenerators(CreateCompilation(source));

        return driver.GetRunResult();
    }

    [UnmanagedCallersOnly]
    private static int ThreeElements(JNINativeInterface_** environment, _jobject* array)
    {
        return 3;
    }

    private sealed record ProbeAccess(Func<JavaClassDefinition> Definition, Func<int> Pings);

    private sealed record RegisteredNative(string Name, string Signature, nint Function);

    private sealed class ReleaseOptions(string? javaRelease) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = javaRelease ?? string.Empty;

            return javaRelease is not null && key == "build_property.JavaRelease";
        }
    }

    private sealed class ReleaseOptionsProvider(string? javaRelease) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new ReleaseOptions(javaRelease);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return GlobalOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return GlobalOptions;
        }
    }
}

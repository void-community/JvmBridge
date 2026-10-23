using JvmBridge.Agents;
using JvmBridge.Generator.Proxies;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>Verifies the typed C# proxies generated for Java classes.</summary>
public sealed class JavaProxySourceWriterTests
{
    private static JavaProxyClass Service => new(
        name: "com/acme/Service",
        [
            new JavaProxyMember(name: "<init>", descriptor: "()V", isStatic: false),
            new JavaProxyMember(name: "<init>", descriptor: "(Ljava/lang/String;I)V", isStatic: false),
            new JavaProxyMember(name: "parse", descriptor: "(Ljava/lang/String;)I", isStatic: true),
            new JavaProxyMember(name: "name", descriptor: "()Ljava/lang/String;", isStatic: false),
            new JavaProxyMember(name: "child", descriptor: "(JZ)Lcom/acme/Service;", isStatic: false),
            new JavaProxyMember(name: "run", descriptor: "([BDC)V", isStatic: false),
            new JavaProxyMember(name: "handle", descriptor: "()V", isStatic: false),
            new JavaProxyMember(name: "service", descriptor: "()V", isStatic: false),
            new JavaProxyMember(name: "send", descriptor: "(Ljava/lang/Object;)V", isStatic: false),
            new JavaProxyMember(name: "send", descriptor: "(Ljava/lang/Runnable;)V", isStatic: false)
        ]
    );

    /// <summary>Every kind of member generates code that compiles against the package, including overloads that differ only in the Java type of an object argument.</summary>
    [Fact]
    public void GeneratesCodeThatCompiles()
    {
        string source = JavaProxySourceWriter.Generate([Service, new JavaProxyClass(name: "Plain", [new JavaProxyMember(name: "ping", descriptor: "()Z", isStatic: true)])]);

        CSharpCompilation compilation = Compile(source);
        Diagnostic[] errors = [.. compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];

        Assert.Empty(errors);
    }

    /// <summary>Primitives and strings map to C# types, other objects and arrays are references, and a constructor creates an instance.</summary>
    [Fact]
    public void MapsDescriptorsToCSharpTypes()
    {
        string source = JavaProxySourceWriter.Generate([Service]);

        Assert.Contains(
            expectedSubstring: "public static int Parse(global::JvmBridge.Runtime.JavaEnvironment environment, string? argument0)",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "public string? Name(global::JvmBridge.Runtime.JavaEnvironment environment)",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "public global::JvmBridge.Runtime.JavaLocalReference Child(global::JvmBridge.Runtime.JavaEnvironment environment, long argument0, bool argument1)",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "public void Run(global::JvmBridge.Runtime.JavaEnvironment environment, nint argument0, double argument1, char argument2)",
            source,
            StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "public static global::JvmBridge.Runtime.JavaLocalReference New(global::JvmBridge.Runtime.JavaEnvironment environment, string? argument0, int argument1)",
            source,
            StringComparison.Ordinal
        );
    }

    /// <summary>The proxy lives in a namespace named after the package, with the class as a struct that wraps a reference.</summary>
    [Fact]
    public void PlacesProxiesInPackageNamespaces()
    {
        string source = JavaProxySourceWriter.Generate([Service]);

        Assert.Contains(expectedSubstring: "namespace JavaProxies.Com.Acme", source, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "internal readonly struct Service", source, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "public const string ClassName = \"com/acme/Service\";", source, StringComparison.Ordinal);
        Assert.Contains(
            expectedSubstring: "namespace JavaProxies",
            JavaProxySourceWriter.Generate([new JavaProxyClass(name: "Plain", [])]),
            StringComparison.Ordinal
        );
    }

    /// <summary>The text description that the build task passes produces the same source as the objects do.</summary>
    [Fact]
    public void ReadsTheTextDescription()
    {
        string description = "class\tcom/acme/Service\nmember\t<init>\t()V\tinstance\nmember\tparse\t(Ljava/lang/String;)I\tstatic\n";

        string fromText = JavaProxySourceWriter.GenerateFromText(description);

        string fromObjects = JavaProxySourceWriter.Generate(
            [
                new JavaProxyClass(
                    name: "com/acme/Service",
                    [new JavaProxyMember(name: "<init>", descriptor: "()V", isStatic: false), new JavaProxyMember(name: "parse", descriptor: "(Ljava/lang/String;)I", isStatic: true)]
                )
            ]
        );

        Assert.Equal(fromObjects, fromText);
        ArgumentException failure = Assert.Throws<ArgumentException>(static () => JavaProxySourceWriter.GenerateFromText(description: "nonsense"));
        Assert.Contains(expectedSubstring: "nonsense", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A member whose name would clash with the struct or its own members is renamed, and overloads with the same C# signature get a suffix from their parameter types.</summary>
    [Fact]
    public void RenamesClashingMembers()
    {
        string source = JavaProxySourceWriter.Generate([Service]);

        Assert.Contains(expectedSubstring: "public void Handle_(", source, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "public void Service_(", source, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "public void Send_Object(", source, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "public void Send_Runnable(", source, StringComparison.Ordinal);
    }

    private static CSharpCompilation Compile(string source)
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(string).Assembly.Location) ?? throw new InvalidOperationException(message: "Runtime assembly directory unavailable.");
        List<MetadataReference> references = [.. Directory.EnumerateFiles(runtimeDirectory, searchPattern: "*.dll").Select(static path => MetadataReference.CreateFromFile(path))];
        references.Add(MetadataReference.CreateFromFile(typeof(JavaAgent).Assembly.Location));

        return CSharpCompilation.Create(
            "Proxies" + Guid.NewGuid().ToString(format: "N"),
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false, nullableContextOptions: NullableContextOptions.Enable)
        );
    }
}

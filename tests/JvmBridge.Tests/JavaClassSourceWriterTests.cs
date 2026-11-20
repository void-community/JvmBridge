using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using JvmBridge.Agents;
using JvmBridge.ClassFiles;
using JvmBridge.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>Verifies the source generated for compiled Java helper classes.</summary>
public sealed class JavaClassSourceWriterTests
{
    private const string Probe = """
        public static class Probe
        {
            public static global::JvmBridge.Agents.JavaClassDefinition Helper() => JavaClasses.Pkg.Helper;
        }
        """;

    /// <summary>The generated source compiles, and its definitions carry the class names, bytes and nested classes.</summary>
    /// <exception cref="InvalidOperationException">The compiled source has no <c>Probe.Helper</c> method to read the definition with.</exception>
    [Fact]
    public void GeneratedSourceCompilesAndHoldsTheBytes()
    {
        byte[] helper = new ClassFileWriter(name: "pkg/Helper").AddNativeMethod(name: "go", descriptor: "()V").ToArray();
        byte[] inner = new ClassFileWriter(name: "pkg/Helper$Inner").AddNativeMethod(name: "go", descriptor: "()V").ToArray();

        string source = JavaClassSourceWriter.Generate(classes:
        [
            new KeyValuePair<string, byte[]>(key: "pkg\\Helper.class", helper),
            new KeyValuePair<string, byte[]>(key: "pkg\\Helper$Inner.class", inner),
        ]);

        Assembly assembly = Compile(source + Probe);

        MethodInfo read = assembly.GetType(name: "Probe", throwOnError: true)?.GetMethod(name: "Helper")
            ?? throw new InvalidOperationException(message: "Probe was not generated.");

        JavaClassDefinition definition = read.CreateDelegate<Func<JavaClassDefinition>>()();

        Assert.Equal(expected: "pkg/Helper", definition.Name);
        Assert.Equal(helper, definition.Bytes.ToArray());
        Assert.Equal(expected: "pkg/Helper$Inner", Assert.Single(definition.NestedClasses).Name);
    }

    /// <summary>A class and a package of the same name do not collide, and output is deterministic.</summary>
    [Fact]
    public void KeepsCollidingNamesApartAndOutputDeterministic()
    {
        KeyValuePair<string, byte[]>[] classes = [Entry(path: "a/Foo.class", name: "a/Foo"), Entry(path: "a/foo/Bar.class", name: "a/foo/Bar")];
        string first = JavaClassSourceWriter.Generate(classes);
        string second = JavaClassSourceWriter.Generate(classes: [.. classes.Reverse()]);
        Assembly built = Compile(first);

        Assert.Equal(first, second);
        Assert.Contains(expectedSubstring: "Foo_", first, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(built.FullName));
    }

    /// <summary>Classes are exposed by package with the first letter capitalized, and nested classes belong to their top-level class.</summary>
    [Fact]
    public void NestsByPackageAndGroupsInnerClasses()
    {
        string source = JavaClassSourceWriter.Generate(classes:
        [
            Entry(path: "pkg/Helper.class", name: "pkg/Helper"),
            Entry(path: "pkg/Helper$1.class", name: "pkg/Helper$1"),
            Entry(path: "pkg/Helper$Inner.class", name: "pkg/Helper$Inner"),
            Entry(path: "other/tools/Utility.class", name: "other/tools/Utility"),
            Entry(path: "Root.class", name: "Root"),
        ]);

        Assert.Contains(expectedSubstring: "internal static class Pkg", source, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "internal static class Tools", source, StringComparison.Ordinal);

        Assert.Contains(expectedSubstring: "internal static global::JvmBridge.Agents.JavaClassDefinition Helper { get; }",
            source,
            StringComparison.Ordinal);

        Assert.Contains(expectedSubstring: "internal static global::JvmBridge.Agents.JavaClassDefinition Root { get; }",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(expectedSubstring: " Inner ", source, StringComparison.Ordinal);
    }

    /// <summary>Names that C# rejects are made valid.</summary>
    [Fact]
    public void ProducesValidIdentifiers()
    {
        // A name gets an upper-case first letter, which also keeps it from being a C# keyword.
        Assert.Equal(expected: "Legacyforgepatcher", JavaClassSourceWriter.ToIdentifier(name: "legacyforgepatcher"));
        Assert.Equal(expected: "Event", JavaClassSourceWriter.ToIdentifier(name: "event"));

        // A leading digit gets an underscore in front, any other invalid character becomes one, and so does an empty name.
        Assert.Equal(expected: "_1st", JavaClassSourceWriter.ToIdentifier(name: "1st"));
        Assert.Equal(expected: "A_b", JavaClassSourceWriter.ToIdentifier(name: "a-b"));
        Assert.Equal(expected: "_", JavaClassSourceWriter.ToIdentifier(name: ""));
    }

    private static Assembly Compile(string source)
    {
        List<MetadataReference> references = RuntimeReferences.Create();

        references.Add(MetadataReference.CreateFromFile(typeof(JavaAgent).Assembly.Location));

        CSharpCompilation compilation = CSharpCompilation.Create("GeneratedClasses" + Guid.NewGuid().ToString(format: "N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        using MemoryStream stream = new();

        EmitResult emitted = compilation.Emit(stream);

        ImmutableArray<Diagnostic> errors = [.. emitted.Diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];

        Assert.True(emitted.Success,
            string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));

        stream.Position = 0;

        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }

    private static KeyValuePair<string, byte[]> Entry(string path, string name)
    {
        return new(path, Encoding.UTF8.GetBytes(name));
    }
}

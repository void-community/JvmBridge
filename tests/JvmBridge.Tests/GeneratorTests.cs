using System.Collections.Immutable;

using JvmBridge.Agents;
using JvmBridge.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>
/// Verifies source generation for NativeAOT JVM agents.
/// </summary>
public sealed class GeneratorTests
{
    /// <summary>Ordinary package consumers do not receive an ABI inspection entry point.</summary>
    [Fact]
    public void DoesNotGenerateAbiInspectorWithoutOptIn()
    {
        VerifyAbiInspector(enabled: false);
    }

    /// <summary>ABI inspection is generated from the consumer's referenced native metadata.</summary>
    [Fact]
    public void GeneratesAbiInspectorFromPackageMetadata()
    {
        VerifyAbiInspector(enabled: true);
    }

    /// <summary>
    /// Verifies that all native agent exports are generated into the consuming assembly.
    /// </summary>
    [Fact]
    public void GeneratesAllAgentExportsIntoConsumerAssembly()
    {
        GeneratorDriverRunResult result = Generate(source: "public class Agent : JvmBridge.Agents.JavaAgent { }");
        Assert.True(result.Diagnostics.IsEmpty);
        Assert.True(result.Results.Length is 1);
        Assert.True(result.Results[index: 0].GeneratedSources.Length is 1);
        string source = result.Results[index: 0].GeneratedSources[index: 0].SourceText.ToString();
        Assert.Contains(expectedSubstring: "Agent_OnLoad", source, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "Agent_OnAttach", source, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "Agent_OnUnload", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that abstract bases are ignored while indirect concrete agents are discovered.
    /// </summary>
    [Fact]
    public void IgnoresAbstractBasesAndFindsIndirectSubclasses()
    {
        GeneratorDriverRunResult result = Generate(source: "public abstract class Base : JvmBridge.Agents.JavaAgent {} public class Agent : Base {}");
        Assert.True(result.Diagnostics.IsEmpty);
        Assert.True(result.Results.Length is 1);
        Assert.True(result.Results[index: 0].GeneratedSources.Length is 1);
    }

    /// <summary>Verifies that repeated base lists on partial declarations identify one agent.</summary>
    [Fact]
    public void PartialDeclarationsGenerateOneAgent()
    {
        GeneratorDriverRunResult result = Generate(source: "public partial class Agent : JvmBridge.Agents.JavaAgent {} public partial class Agent : JvmBridge.Agents.JavaAgent {}");
        Assert.True(result.Diagnostics.IsEmpty);
        Assert.True(result.Results.Length is 1);
        Assert.True(result.Results[index: 0].GeneratedSources.Length is 1);
    }

    /// <summary>
    /// Verifies that invalid agent declarations produce the generator diagnostic.
    /// </summary>
    /// <param name="source">The invalid consumer source to compile.</param>
    [Theory]
    [InlineData("public abstract class Agent : JvmBridge.Agents.JavaAgent { }")]
    [InlineData("public class Agent<T> : JvmBridge.Agents.JavaAgent { }")]
    [InlineData("public class Agent { }")]
    [InlineData("public class Agent : JvmBridge.Agents.JavaAgent { private Agent() { } }")]
    [InlineData("public class A : JvmBridge.Agents.JavaAgent {} public class B : JvmBridge.Agents.JavaAgent {}")]
    [InlineData("public class Outer { protected class Agent : JvmBridge.Agents.JavaAgent {} }")]
    [InlineData("file class Agent : JvmBridge.Agents.JavaAgent {}")]
    public void RejectsInvalidAgents(string source)
    {
        ImmutableArray<Diagnostic> diagnostics = Generate(source).Diagnostics;
        Assert.True(ContainsDiagnostic(diagnostics, identifier: "JVMB001"));
    }

    /// <summary>
    /// Verifies that an agent type requires explicit agent-project configuration.
    /// </summary>
    [Fact]
    public void RequiresAgentProjectConfiguration()
    {
        ImmutableArray<Diagnostic> diagnostics = Generate(source: "public class Agent : JvmBridge.Agents.JavaAgent {}", enabled: false).Diagnostics;
        Assert.True(ContainsDiagnostic(diagnostics, identifier: "JVMB001"));
    }

    private static bool ContainsDiagnostic(ImmutableArray<Diagnostic> diagnostics, string identifier)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Id == identifier)
                return true;
        }

        return false;
    }

    private static bool ContainsError(ImmutableArray<Diagnostic> diagnostics)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
                return true;
        }

        return false;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(string).Assembly.Location) ?? throw new InvalidOperationException(message: "Runtime assembly directory unavailable.");
        List<MetadataReference> references = [];

        foreach (string assemblyPath in Directory.EnumerateFiles(runtimeDirectory, searchPattern: "*.dll"))
            references.Add(MetadataReference.CreateFromFile(assemblyPath));

        references.Add(MetadataReference.CreateFromFile(typeof(JavaAgent).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "TestAgent",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
        );
    }

    private static GeneratorDriverRunResult Generate(string source, bool enabled = true)
    {
        CSharpCompilation compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new AgentGenerator().AsSourceGenerator()], optionsProvider: new OptionsProvider(enabled));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        if (!ContainsError(generatorDiagnostics))
            Assert.False(ContainsError(output.GetDiagnostics()));

        return result;
    }

    private static void VerifyAbiInspector(bool enabled)
    {
        CSharpCompilation compilation = CreateCompilation(string.Empty);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new AbiInspectorGenerator().AsSourceGenerator()],
            optionsProvider: new OptionsProvider(enabled, property: "build_property.JvmBridgeAbiInspector")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics, TestContext.Current.CancellationToken);
        Assert.False(ContainsError(diagnostics));
        Assert.False(ContainsError(output.GetDiagnostics(TestContext.Current.CancellationToken)));
        GeneratorDriverRunResult result = driver.GetRunResult();
        Assert.True(result.Results[index: 0].GeneratedSources.Length == (enabled ? 1 : 0));

        if (!enabled)
            return;

        string source = result.Results[index: 0].GeneratedSources[index: 0].SourceText.ToString();
        Assert.Contains(expectedSubstring: "offset.jvmtiHeapCallbacks.reserved15", source, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "sizeof(global::JvmBridge.Native.jvalue)", source, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "capability.can_retransform_classes", source, StringComparison.Ordinal);
    }

    private sealed class Options(bool enabled, string property) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = enabled ? "true" : "false";

            return key == property;
        }
    }

    private sealed class OptionsProvider(bool enabled, string property = "build_property.JvmBridgeAgent") : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(enabled, property);
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

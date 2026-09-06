using JvmBridge.Agents;
using JvmBridge.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace JvmBridge.Tests;

public sealed class GeneratorTests
{
    private static GeneratorDriverRunResult Generate(string source, bool enabled = true)
    {
        string[] assemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime assembly paths unavailable.")).Split(Path.PathSeparator);
        List<MetadataReference> references = assemblies.Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>().ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(JavaAgent).Assembly.Location));
        CSharpCompilation compilation = CSharpCompilation.Create("TestAgent", [CSharpSyntaxTree.ParseText(source)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new AgentGenerator().AsSourceGenerator()], optionsProvider: new OptionsProvider(enabled));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        if (!result.Diagnostics.Any(value => value.Severity == DiagnosticSeverity.Error))
            Assert.Empty(output.GetDiagnostics().Where(value => value.Severity == DiagnosticSeverity.Error));
        return result;
    }

    [Fact]
    public void GeneratesAllAgentExportsIntoConsumerAssembly()
    {
        GeneratorDriverRunResult result = Generate("public class Agent : JvmBridge.Agents.JavaAgent { }");
        Assert.Empty(result.Diagnostics);
        string source = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        Assert.Contains("Agent_OnLoad", source);
        Assert.Contains("Agent_OnAttach", source);
        Assert.Contains("Agent_OnUnload", source);
    }

    [Fact]
    public void IgnoresAbstractBasesAndFindsIndirectSubclasses()
    {
        GeneratorDriverRunResult result = Generate("public abstract class Base : JvmBridge.Agents.JavaAgent {} public class Agent : Base {}");
        Assert.Empty(result.Diagnostics);
        Assert.Single(Assert.Single(result.Results).GeneratedSources);
    }

    [Theory]
    [InlineData("public abstract class Agent : JvmBridge.Agents.JavaAgent { }")]
    [InlineData("public class Agent<T> : JvmBridge.Agents.JavaAgent { }")]
    [InlineData("public class Agent { }")]
    [InlineData("public class Agent : JvmBridge.Agents.JavaAgent { private Agent() { } }")]
    [InlineData("public class A : JvmBridge.Agents.JavaAgent {} public class B : JvmBridge.Agents.JavaAgent {}")]
    public void RejectsInvalidAgents(string source) => Assert.Contains(Generate(source).Diagnostics, value => value.Id == "JVMB001");

    [Fact]
    public void RequiresAgentProjectConfiguration() => Assert.Contains(Generate("public class Agent : JvmBridge.Agents.JavaAgent {}", false).Diagnostics, value => value.Id == "JVMB001");

    private sealed class OptionsProvider(bool enabled) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(enabled);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }
    private sealed class Options(bool enabled) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) { value = enabled ? "true" : "false"; return key == "build_property.JvmBridgeAgent"; }
    }
}

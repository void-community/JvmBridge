using JvmBridge.Agents;
using JvmBridge.Tool.Commands;

using Xunit;

namespace JvmBridge.Tool.Tests;

/// <summary>Verifies <c>jvmbridge patch --dry-run</c>, which runs agents against an offline JVM.</summary>
public sealed class PatchCommandTests
{
    /// <summary>An agent can request capabilities, subscribe to events and define helper classes in Configure; the offline JVM accepts all of it, and denies what it cannot do with an error the agent can see.</summary>
    [Fact]
    public void AcceptsTheCallsAnAgentMakesInConfigure()
    {
        using StringWriter output = new();

        using StringWriter error = new();

        int status = PatchCommand.RunAgent(new BusyAgent(), [ToolFixtures.Sample], string.Empty, disassemble: false, output, error);

        Assert.Equal(expected: 0, status);
        Assert.Contains(expectedSubstring: "1 patch(es) registered", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>An agent that registers nothing is explained, and an agent whose Configure throws is reported with the patches it registered first.</summary>
    [Fact]
    public void ExplainsAgentsThatRegisterNothingOrThrow()
    {
        using StringWriter empty = new();

        using StringWriter thrown = new();

        using StringWriter error = new();

        int quiet = PatchCommand.RunAgent(new QuietAgent(), [ToolFixtures.Sample], string.Empty, disassemble: false, empty, error);
        int failing = PatchCommand.RunAgent(new ThrowingAgent(), [ToolFixtures.Sample], string.Empty, disassemble: false, thrown, error);

        Assert.Equal(expected: 0, quiet);
        Assert.Contains(expectedSubstring: "0 patch(es) registered", empty.ToString(), StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "not visible to a dry run", empty.ToString(), StringComparison.Ordinal);
        Assert.Equal(expected: 1, failing);
        Assert.Contains(expectedSubstring: "Configure threw InvalidOperationException: configure failed", thrown.ToString(), StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "1 patch(es) registered", thrown.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The options reach the agent, a patch that matches by supertype sees the classes given as a whole, and --disassemble prints the patched class.</summary>
    [Fact]
    public void PassesOptionsAndKnowsTheClassesGiven()
    {
        using StringWriter output = new();

        using StringWriter error = new();

        int status = PatchCommand.RunAgent(new OptionsAgent(), [ToolFixtures.ClassFiles], options: "pkg=fixture", disassemble: true, output, error);

        Assert.Equal(expected: 0, status);
        Assert.Contains(expectedSubstring: "Annotated.class <- any class extending java/lang/Runnable", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "ldc", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The patches an agent registers in Configure are applied to the classes given, with the patch that matched each class and what it changed.</summary>
    [Fact]
    public void PreviewsThePatchesOfAnAgent()
    {
        using StringWriter output = new();

        using StringWriter error = new();

        int status = PatchCommand.RunAgent(new ConstantAgent(), [ToolFixtures.ClassFiles], string.Empty, disassemble: false, output, error);

        Assert.Equal(expected: 0, status);
        Assert.Empty(error.ToString());
        Assert.Contains(expectedSubstring: "1 patch(es) registered by Configure", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "Sample.class <- fixture/Sample: patched fixture/Sample", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "\"hello\" replaced", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "1 class(es) would change.", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>An assembly with several agents, and a path that does not exist, are refused with a message.</summary>
    [Fact]
    public void RefusesAmbiguousAssembliesAndMissingClasses()
    {
        using StringWriter output = new();

        using StringWriter error = new();

        int ambiguous = PatchCommand.Run(typeof(ConstantAgent).Assembly, [ToolFixtures.Sample], string.Empty, disassemble: false, output, error);

        int missing = PatchCommand.RunAgent(
            new ConstantAgent(),
            [Path.Combine(ToolFixtures.ClassFiles, path2: "nothing.jar")],
            string.Empty,
            disassemble: false,
            output,
            error
        );

        Assert.Equal(expected: 1, ambiguous);
        Assert.Equal(expected: 1, missing);
        Assert.Contains(expectedSubstring: "exactly one concrete JavaAgent, found 5", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "nothing.jar does not exist", error.ToString(), StringComparison.Ordinal);
    }

    private sealed class BusyAgent : JavaAgent
    {
        /// <inheritdoc/>
        public override void Configure(AgentContext context)
        {
            if (context.TryRequest(JavaCapability.RedefineClasses))
                context.Log(context.Events.Subscribe(JavaEvents.ThreadStart | JavaEvents.ClassLoad).ToString());

            context.Patch(ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "hello", newValue: "busy"));
        }
    }

    private sealed class ConstantAgent : JavaAgent
    {
        /// <inheritdoc/>
        public override void Configure(AgentContext context)
        {
            context.Patch(ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "hello", newValue: "bye"));
        }
    }

    private sealed class OptionsAgent : JavaAgent
    {
        /// <inheritdoc/>
        public override void Configure(AgentContext context)
        {
            string package = context.ParsedOptions.TryGetValue(key: "pkg", out string? value) ? value : "none";

            context.Patch(ClassPatch.ForSubclassesOf(superType: "java/lang/Runnable").ReplaceConstant(oldValue: "item", newValue: "entry"));
            context.Patch(ClassPatch.ForPackage(package).ReplaceConstant(oldValue: "hello again", newValue: "again"));
        }
    }

    private sealed class QuietAgent : JavaAgent;

    private sealed class ThrowingAgent : JavaAgent
    {
        /// <inheritdoc/>
        public override void Configure(AgentContext context)
        {
            context.Patch(ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "hello", newValue: "bye"));

            throw new InvalidOperationException(message: "configure failed");
        }
    }
}

using JvmBridge.Agents;
using JvmBridge.Native;
using JvmBridge.Runtime;

using Xunit;

namespace JvmBridge.Tests.Agents;

/// <summary>Verifies that loading an agent library again into a JVM where it runs is a command for the running agent.</summary>
public sealed unsafe class AgentCommandTests
{
    /// <summary>A command the agent cannot handle makes the attaching tool see a failed load, and the failure is contained.</summary>
    [Fact]
    public void AFailingCommandReportsAnError()
    {
        using FakeJvm jvm = new();

        RecordingAgent agent = new() { FailCommands = true };
        int loaded = AgentRuntime.Start(() => agent, jvm.Machine, options: 0, attached: false);

        int result = Attach(jvm, static () => new RecordingAgent(), command: "boom");

        Assert.Equal(Methods.JNI_OK, loaded);
        Assert.Equal(Methods.JNI_ERR, result);
        Assert.Equal(["boom"], agent.Commands);
    }

    /// <summary>A second attach calls the running agent with the option text and starts no second agent.</summary>
    [Fact]
    public void ASecondAttachIsACommandForTheRunningAgent()
    {
        using FakeJvm jvm = new();

        RecordingAgent agent = new();
        int loaded = AgentRuntime.Start(() => agent, jvm.Machine, options: 0, attached: false);

        int result = Attach(jvm, static () => throw new InvalidOperationException(message: "A second agent must not be created."), command: "unpatch=all");

        Assert.Equal(Methods.JNI_OK, loaded);
        Assert.Equal(Methods.JNI_OK, result);
        Assert.Equal(["unpatch=all"], agent.Commands);
        Assert.Equal(expected: 0, agent.Attaches);
    }

    /// <summary>The first attach into a JVM starts the agent as before.</summary>
    [Fact]
    public void AnAttachWithoutARunningAgentStartsOne()
    {
        using FakeJvm jvm = new();

        RecordingAgent agent = new();

        int result = Attach(jvm, () => agent, command: "start");

        Assert.Equal(Methods.JNI_OK, result);
        Assert.Equal(expected: 1, agent.Attaches);
        Assert.Empty(agent.Commands);
    }

    /// <summary>An agent that does not handle commands ignores them without failing the attach.</summary>
    [Fact]
    public void TheDefaultHandlerIgnoresCommands()
    {
        using FakeJvm jvm = new();

        int loaded = AgentRuntime.Start(static () => new PlainAgent(), jvm.Machine, options: 0, attached: false);

        int result = Attach(jvm, static () => new PlainAgent(), command: "anything");

        Assert.Equal(Methods.JNI_OK, loaded);
        Assert.Equal(Methods.JNI_OK, result);
    }

    private static int Attach(FakeJvm jvm, Func<JavaAgent> factory, string command)
    {
        fixed (byte* text = ModifiedUtf8.Encode(command))
            return AgentRuntime.Start(factory, jvm.Machine, (nint)text, attached: true);
    }

    private sealed class PlainAgent : JavaAgent;

    private sealed class RecordingAgent : JavaAgent
    {
        internal int Attaches { get; private set; }

        internal List<string> Commands { get; } = [];

        internal bool FailCommands { get; init; }

        public override void OnAttach(AgentContext context)
        {
            Attaches++;
        }

        public override void OnCommand(AgentContext context, string command)
        {
            Commands.Add(command);

            if (FailCommands)
                throw new InvalidOperationException(message: "The command failed.");
        }
    }
}

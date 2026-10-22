using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using JvmBridge.Agents;
using JvmBridge.Native;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>Verifies agent log levels, the replaceable sink and the level taken from the agent options.</summary>
[Collection(nameof(AgentLogTests))]
public sealed unsafe class AgentLogTests
{
    [ThreadStatic]
    private static nint s_tooling;

    /// <summary>A sink that throws does not fail the logging callback.</summary>
    [Fact]
    public void ASinkThatThrowsIsContained()
    {
        AgentLogSink previous = AgentLog.Sink;

        try
        {
            AgentLog.Sink = static (level, source, message) => throw new InvalidOperationException("sink failure for " + source);

            AgentLog.Write(AgentLogLevel.Error, source: "test", message: "still delivered to stderr");
        }
        finally
        {
            AgentLog.Sink = previous;
        }
    }

    /// <summary>A line reaches the installed sink with its level, source and text.</summary>
    [Fact]
    public void LinesReachTheSink()
    {
        ConcurrentQueue<LogLine> lines = [];
        string source = "sink-" + Guid.NewGuid().ToString(format: "N");
        AgentLogSink previous = AgentLog.Sink;

        try
        {
            AgentLog.Sink = (level, name, message) => lines.Enqueue(new LogLine(level, name, message));
            AgentLog.Write(AgentLogLevel.Warning, source, message: "careful");

            Assert.Contains(lines, line => line.Source == source && line.Level == AgentLogLevel.Warning && line.Message == "careful");
        }
        finally
        {
            AgentLog.Sink = previous;
        }
    }

    /// <summary>Setting the sink to null restores the default one.</summary>
    [Fact]
    public void NullRestoresTheDefaultSink()
    {
        AgentLogSink previous = AgentLog.Sink;

        try
        {
            AgentLog.Sink = static (level, source, message) => { };
            AgentLog.Sink = null;

            Assert.NotNull(AgentLog.Sink);
            AgentLog.Write(AgentLogLevel.Information, source: "test", message: "default sink line");
        }
        finally
        {
            AgentLog.Sink = previous;
        }
    }

    /// <summary>Level names parse ignoring case, including the short and off forms; anything else keeps the fallback.</summary>
    [Fact]
    public void ParsesLevelNames()
    {
        (string? Text, AgentLogLevel Expected)[] cases =
        [
            ("trace", AgentLogLevel.Trace),
            ("DEBUG", AgentLogLevel.Debug),
            ("info", AgentLogLevel.Information),
            ("Information", AgentLogLevel.Information),
            ("warn", AgentLogLevel.Warning),
            ("error", AgentLogLevel.Error),
            ("none", AgentLogLevel.None),
            ("off", AgentLogLevel.None),
            ("loud", AgentLogLevel.Warning),
            (null, AgentLogLevel.Warning)
        ];

        foreach ((string? text, AgentLogLevel expected) in cases)
            Assert.Equal(expected, AgentLog.ParseLevel(text, AgentLogLevel.Warning));
    }

    /// <summary>The context takes its level from the log option, drops lines below it and drops everything at none.</summary>
    [Fact]
    public void TheContextFiltersByTheLogOption()
    {
        ConcurrentQueue<LogLine> lines = [];
        AgentLogSink previous = AgentLog.Sink;
        jvmtiInterface_1_ toolingTable = new() { GetVersionNumber = &Version };
        jvmtiInterface_1_* toolingPointer = &toolingTable;
        s_tooling = (nint)(&toolingPointer);
        JNIInvokeInterface_ machineTable = new() { GetEnv = &GetEnvironment };
        JNIInvokeInterface_* machinePointer = &machineTable;
        string name = "filter-" + Guid.NewGuid().ToString(format: "N");

        try
        {
            AgentLog.Sink = (level, source, message) => lines.Enqueue(new LogLine(level, source, message));
            AgentContext quiet = new(new EmptyAgent(), (nint)(&machinePointer), options: "log=warn", attached: false, name);
            AgentContext defaults = new(new EmptyAgent(), (nint)(&machinePointer), options: "other", attached: false, name + "-default");

            quiet.Log(message: "information is dropped");
            quiet.Log(AgentLogLevel.Debug, message: "debug is dropped");
            quiet.Log(AgentLogLevel.Error, message: "error is kept");
            defaults.Log(message: "information is kept");
            defaults.Log(AgentLogLevel.Trace, message: "trace is dropped");
            quiet.LogLevel = AgentLogLevel.None;
            quiet.Log(AgentLogLevel.Error, message: "none drops everything");

            Assert.Equal(AgentLogLevel.Information, defaults.LogLevel);
            Assert.Equal(["error is kept"], lines.Where(line => line.Source == name).Select(line => line.Message));
            Assert.Equal(["information is kept"], lines.Where(line => line.Source == name + "-default").Select(line => line.Message));
            Assert.False(defaults.IsLogEnabled(AgentLogLevel.Debug));
            Assert.True(defaults.IsLogEnabled(AgentLogLevel.Warning));
            Assert.Equal(expected: "other", defaults.ParsedOptions.Text);
        }
        finally
        {
            AgentLog.Sink = previous;
            s_tooling = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static int GetEnvironment(JNIInvokeInterface_** machine, void** environment, int version)
    {
        *environment = (void*)s_tooling;

        return Methods.JNI_OK;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError Version(jvmtiInterface_1_** environment, int* version)
    {
        *version = 0x30010000;

        return jvmtiError.JVMTI_ERROR_NONE;
    }

    private sealed class EmptyAgent : JavaAgent;

    private sealed record LogLine(AgentLogLevel Level, string Source, string Message);
}

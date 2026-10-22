using JvmBridge.Agents;

using Microsoft.Extensions.Logging;

using Xunit;

namespace JvmBridge.Logging.Tests;

/// <summary>Verifies sending the agent's own log lines to an ILogger.</summary>
[Collection(nameof(RedirectTests))]
public sealed class RedirectTests
{
    /// <summary>Lines go to the logger with the agent name as a prefix, and a disabled level is dropped.</summary>
    [Fact]
    public void LinesGoToTheLogger()
    {
        RecordingLogger logger = new(LogLevel.Warning);
        AgentLogSink previous = AgentLog.Sink;

        try
        {
            AgentLogging.Redirect(logger);

            AgentLog.Sink(AgentLogLevel.Error, source: "MyAgent", message: "failed");
            AgentLog.Sink(AgentLogLevel.Information, source: "MyAgent", message: "dropped by the logger");
            AgentLog.Sink(AgentLogLevel.None, source: "MyAgent", message: "never logged");

            Assert.Equal(["Error|MyAgent: failed"], logger.Lines);
        }
        finally
        {
            AgentLog.Sink = previous;
        }
    }

    /// <summary>Redirecting to null restores the default sink.</summary>
    [Fact]
    public void NullRestoresStandardError()
    {
        RecordingLogger logger = new(LogLevel.Trace);
        AgentLogSink previous = AgentLog.Sink;

        try
        {
            AgentLogging.Redirect(logger);
            AgentLogging.Redirect(logger: null);
            AgentLog.Sink(AgentLogLevel.Information, source: "MyAgent", message: "to stderr");

            Assert.Empty(logger.Lines);
        }
        finally
        {
            AgentLog.Sink = previous;
        }
    }

    private sealed class RecordingLogger(LogLevel minimum) : ILogger
    {
        internal List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= minimum && logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(logLevel.ToString() + "|" + formatter(state, exception));
        }
    }
}

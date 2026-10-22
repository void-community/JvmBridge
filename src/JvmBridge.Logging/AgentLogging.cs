using JvmBridge.Agents;

using Microsoft.Extensions.Logging;

namespace JvmBridge.Logging;

/// <summary>Sends the agent's own log lines to an <see cref="ILogger"/>.</summary>
public static class AgentLogging
{
    /// <summary>Replaces <see cref="AgentLog.Sink"/> so that everything JvmBridge logs, including failures that the runtime contained, goes to a logger with the agent's name as a prefix.</summary>
    /// <param name="logger">The logger to receive the lines, or <see langword="null"/> to restore standard error.</param>
    public static void Redirect(ILogger? logger)
    {
        if (logger is null)
        {
            AgentLog.Sink = null;

            return;
        }

        AgentLog.Sink = (level, source, message) => ForwardToLogger(logger, level, source, message);
    }

    private static void ForwardToLogger(ILogger logger, AgentLogLevel level, string source, string message)
    {
        LogLevel target = AgentLogLevels.ToLogLevel(level);

        if (target != LogLevel.None && logger.IsEnabled(target))
            logger.Log(target, eventId: default, source + ": " + message, exception: null, static (state, exception) => state);
    }
}

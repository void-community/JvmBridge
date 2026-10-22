using JvmBridge.Agents;

using Microsoft.Extensions.Logging;

namespace JvmBridge.Logging;

/// <summary>
/// Maps between the agent's log levels and <see cref="LogLevel"/>. Trace through Error share their numeric values in both enumerations, so the mapping is arithmetic.
/// </summary>
public static class AgentLogLevels
{
    /// <summary>Converts a <see cref="LogLevel"/> to an agent level. <see cref="LogLevel.Critical"/> logs as <see cref="AgentLogLevel.Error"/>.</summary>
    /// <param name="level">The Microsoft.Extensions.Logging level.</param>
    /// <returns>The agent level.</returns>
    public static AgentLogLevel ToAgentLevel(LogLevel level)
    {
        return level is >= LogLevel.Trace and <= LogLevel.Critical ? (AgentLogLevel)Math.Min((int)level, (int)AgentLogLevel.Error) : AgentLogLevel.None;
    }

    /// <summary>Converts an agent level to a <see cref="LogLevel"/>.</summary>
    /// <param name="level">The agent level.</param>
    /// <returns>The Microsoft.Extensions.Logging level.</returns>
    public static LogLevel ToLogLevel(AgentLogLevel level)
    {
        return level is >= AgentLogLevel.Trace and <= AgentLogLevel.Error ? (LogLevel)(int)level : LogLevel.None;
    }
}

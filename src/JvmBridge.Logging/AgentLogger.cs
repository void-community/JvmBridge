using JvmBridge.Agents;

using Microsoft.Extensions.Logging;

namespace JvmBridge.Logging;

/// <summary>An <see cref="ILogger"/> that writes to the agent's log.</summary>
internal sealed class AgentLogger : ILogger
{
    private readonly string _category;
    private readonly Func<AgentLogLevel, bool> _isEnabled;
    private readonly Action<AgentLogLevel, string> _write;

    internal AgentLogger(string category, Func<AgentLogLevel, bool> isEnabled, Action<AgentLogLevel, string> write)
    {
        _category = category;
        _isEnabled = isEnabled;
        _write = write;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _isEnabled(AgentLogLevels.ToAgentLevel(logLevel));
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        AgentLogLevel level = AgentLogLevels.ToAgentLevel(logLevel);

        if (!_isEnabled(level))
            return;

        string message = formatter(state, exception);
        string text = exception is null ? message : message + Environment.NewLine + exception;
        _write(level, _category.Length == 0 ? text : _category + ": " + text);
    }
}

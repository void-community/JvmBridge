using JvmBridge.Agents;

using Microsoft.Extensions.Logging;

namespace JvmBridge.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> whose loggers write to the agent's log, so code that takes an <see cref="ILogger"/> can run inside an agent. Lines below <see cref="AgentContext.LogLevel"/> are dropped, and the
/// rest go through <see cref="AgentContext.Log(AgentLogLevel, string)"/> to <see cref="AgentLog.Sink"/>, with the logger's category as a prefix.
/// </summary>
public sealed class AgentLoggerProvider : ILoggerProvider
{
    private readonly AgentContext _context;

    /// <summary>Initializes a provider for an agent.</summary>
    /// <param name="context">The agent whose log the loggers write to.</param>
    public AgentLoggerProvider(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        return new AgentLogger(categoryName ?? string.Empty, _context.IsLogEnabled, _context.Log);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Loggers hold only the context, which the runtime owns.
    }
}

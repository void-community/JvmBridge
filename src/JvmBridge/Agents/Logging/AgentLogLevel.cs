namespace JvmBridge.Agents;

/// <summary>The severity of an agent log line. A context drops lines below its <see cref="AgentContext.LogLevel"/>.</summary>
public enum AgentLogLevel
{
    /// <summary>Very detailed diagnostics, such as every class the agent inspects.</summary>
    Trace = 0,

    /// <summary>Diagnostics that help to debug an agent.</summary>
    Debug = 1,

    /// <summary>Normal operation, such as what a patch changed. This is the default level.</summary>
    Information = 2,

    /// <summary>Something unexpected that the agent worked around.</summary>
    Warning = 3,

    /// <summary>An operation failed.</summary>
    Error = 4,

    /// <summary>Drops every line. Use it as a context's level to silence the agent.</summary>
    None = 5
}

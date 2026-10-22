namespace JvmBridge.Agents;

/// <summary>Receives the log lines of the agents in this process. It runs on whatever thread logged, including JVM callback threads, so it must be fast, thread-safe and must not call back into the JVM.</summary>
/// <param name="level">The severity of the line.</param>
/// <param name="source">The agent's <see cref="AgentContext.Name"/>, or <c>JvmBridge</c> for failures that the runtime contained.</param>
/// <param name="message">The text without the source prefix.</param>
public delegate void AgentLogSink(AgentLogLevel level, string source, string message);

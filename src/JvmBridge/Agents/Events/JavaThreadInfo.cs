namespace JvmBridge.Agents;

/// <summary>What the JVM knows about a Java thread.</summary>
/// <param name="Name">The thread name.</param>
/// <param name="Priority">The thread priority.</param>
/// <param name="IsDaemon">Whether the thread is a daemon.</param>
public readonly record struct JavaThreadInfo(string Name, int Priority, bool IsDaemon);

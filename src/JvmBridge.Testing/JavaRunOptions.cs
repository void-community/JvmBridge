namespace JvmBridge.Testing;

/// <summary>How <see cref="Jdk.RunAsync"/> starts the JVM: program and JVM arguments, agent options, checked JNI, signal chaining and the time limit.</summary>
public sealed class JavaRunOptions
{
    /// <summary>Gets the options passed to the agent after <c>=</c> in <c>-agentpath</c> or <c>-javaagent</c>.</summary>
    public string? AgentOptions { get; init; }

    /// <summary>Gets the program arguments, which follow the main class name.</summary>
    public IList<string> Arguments { get; } = [];

    /// <summary>Gets whether to run with <c>-Xcheck:jni</c>, which makes the JVM verify every JNI call.</summary>
    public bool CheckJni { get; init; }

    /// <summary>Gets additional JVM arguments, which come before the class path.</summary>
    public IList<string> JvmArguments { get; } = [];

    /// <summary>Gets whether HotSpot on Linux and macOS preloads <c>libjsig</c> when an agent library is loaded with <c>-agentpath</c>, so the agent's signal handlers chain instead of replacing the JVM's. A jar is never run with it.</summary>
    public bool PreloadSignalChaining { get; init; } = true;

    /// <summary>Gets how long the program may run before it is stopped and the run fails.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(minutes: 2);
}

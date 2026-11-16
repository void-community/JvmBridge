namespace JvmBridge.Testing;

/// <summary>
/// How <see cref="Jdk.RunAsync"/> and <see cref="Jdk.StartAsync"/> start the JVM: program and JVM arguments, agent options, checked JNI, signal chaining, the working directory and the time limit. A copy made with <c>with</c> shares the argument
/// lists unless it sets them, as in <c>options with { Arguments = ["--installServer"] }</c>.
/// </summary>
public sealed record JavaRunOptions
{
    /// <summary>Gets the options passed to the agent after <c>=</c> in <c>-agentpath</c> or <c>-javaagent</c>.</summary>
    public string? AgentOptions { get; init; }

    /// <summary>Gets the program arguments, which follow the main class name.</summary>
    public IList<string> Arguments { get; init; } = [];

    /// <summary>Gets whether to run with <c>-Xcheck:jni</c>, which makes the JVM verify every JNI call.</summary>
    public bool CheckJni { get; init; }

    /// <summary>Gets additional JVM arguments, which come before the class path.</summary>
    public IList<string> JvmArguments { get; init; } = [];

    /// <summary>Gets whether HotSpot on Linux and macOS preloads <c>libjsig</c> when an agent library is loaded with <c>-agentpath</c>, so the agent's signal handlers chain instead of replacing the JVM's. A jar is never run with it.</summary>
    public bool PreloadSignalChaining { get; init; } = true;

    /// <summary>Gets how long the program may run in all before it is ended and the run, or the wait, fails.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(minutes: 2);

    /// <summary>
    /// Gets the directory the JVM starts in, which it reports as <c>user.dir</c> and where it resolves relative paths, those of the class path and the arguments included; <see langword="null"/> starts it in the test's working directory. The
    /// directory must exist.
    /// </summary>
    public string? WorkingDirectory { get; init; }
}

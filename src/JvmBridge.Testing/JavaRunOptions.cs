namespace JvmBridge.Testing;

/// <summary>
/// How <see cref="Jdk.RunAsync"/> and <see cref="Jdk.StartAsync"/> start the JVM: program and JVM arguments, agent options, checked JNI, signal chaining, the working directory and the time limit. A copy made with <c>with</c> shares the argument
/// lists unless it sets them, as in <c>options with { Arguments = ["--installServer"] }</c>.
/// </summary>
public sealed record JavaRunOptions
{
    /// <summary>Gets or sets the options passed to the agent after <c>=</c> in <c>-agentpath</c> or <c>-javaagent</c>.</summary>
    /// <value>The text the agent receives as its options; <see langword="null"/>, the default, passes none.</value>
    public string? AgentOptions { get; init; }

    /// <summary>Gets or sets the program arguments, which follow the main class name.</summary>
    /// <value>The arguments in the order the program receives them; empty by default.</value>
    public IList<string> Arguments { get; init; } = [];

    /// <summary>Gets or sets whether to run with <c>-Xcheck:jni</c>, which makes the JVM verify every JNI call.</summary>
    /// <value><see langword="true"/> if the JVM checks the JNI calls and reports misuse in its output; otherwise, <see langword="false"/>.</value>
    public bool CheckJni { get; init; }

    /// <summary>Gets or sets additional JVM arguments, which come before the class path.</summary>
    /// <value>The arguments in the order they are passed, ahead of the one that loads the agent; empty by default.</value>
    public IList<string> JvmArguments { get; init; } = [];

    /// <summary>Gets or sets whether HotSpot on Linux and macOS preloads <c>libjsig</c> when an agent library is loaded with <c>-agentpath</c>, so the agent's signal handlers chain instead of replacing the JVM's. A jar is never run with it. It is on unless it is set otherwise.</summary>
    /// <value><see langword="true"/> if <c>libjsig</c> is preloaded where it applies; otherwise, <see langword="false"/>.</value>
    public bool PreloadSignalChaining { get; init; } = true;

    /// <summary>Gets or sets how long the program may run in all before it is ended and the run, or the wait, fails.</summary>
    /// <value>A duration counted from the start of the JVM; two minutes by default.</value>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(minutes: 2);

    /// <summary>
    /// Gets or sets the directory the JVM starts in, which it reports as <c>user.dir</c> and where it resolves relative paths, those of the class path and the arguments included; <see langword="null"/> starts it in the test's working directory. The
    /// directory must exist.
    /// </summary>
    /// <value>An absolute path, or one relative to the working directory of the test; <see langword="null"/> by default.</value>
    public string? WorkingDirectory { get; init; }
}

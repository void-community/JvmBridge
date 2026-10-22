namespace JvmBridge.Testing;

/// <summary>The outcome of running a Java program.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Output">Standard output and standard error, interleaved in the order they arrived.</param>
public sealed record JavaRun(int ExitCode, string Output);

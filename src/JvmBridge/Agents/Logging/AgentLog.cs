using System.Diagnostics.CodeAnalysis;

namespace JvmBridge.Agents;

/// <summary>
/// Routes the log lines of this agent library. The default sink writes <c>source: message</c> to standard error, whatever the level, without <see cref="Console"/> so it needs no ICU. Replace it to send lines elsewhere,
/// for example to an <c>ILogger</c> through the JvmBridge.Logging package.
/// </summary>
public static class AgentLog
{
    private static AgentLogSink _sink = WriteToStandardError;

    /// <summary>Gets or sets the sink that receives every line. Setting <see langword="null"/> restores the default. A sink that throws does not fail the callback that logged: the line goes to standard error instead.</summary>
    [AllowNull]
    public static AgentLogSink Sink
    {
        get => Volatile.Read(ref _sink);
        set => Volatile.Write(ref _sink, value ?? WriteToStandardError);
    }

    internal static AgentLogLevel ParseLevel(string? text, AgentLogLevel fallback)
    {
        if (text is null)
            return fallback;

        foreach ((string name, AgentLogLevel level) in Names)
        {
            if (string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
                return level;
        }

        return fallback;
    }

    internal static void Write(AgentLogLevel level, string source, string message)
    {
        try
        {
            Sink(level, source, message);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // A sink must never fail the callback that logged.
            WriteToStandardError(level, source, message);
        }
    }

    private static readonly (string Name, AgentLogLevel Level)[] Names =
    [
        ("trace", AgentLogLevel.Trace),
        ("debug", AgentLogLevel.Debug),
        ("info", AgentLogLevel.Information),
        ("information", AgentLogLevel.Information),
        ("warn", AgentLogLevel.Warning),
        ("warning", AgentLogLevel.Warning),
        ("error", AgentLogLevel.Error),
        ("none", AgentLogLevel.None),
        ("off", AgentLogLevel.None)
    ];

    private static void WriteToStandardError(AgentLogLevel level, string source, string message)
    {
        StandardError.WriteLine($"{source}: {message}");
    }
}

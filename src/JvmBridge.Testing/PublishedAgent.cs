using System.Runtime.InteropServices;

using Xunit;

namespace JvmBridge.Testing;

/// <summary>A NativeAOT agent that has been published for the current platform: its native library, which <see cref="Jdk.RunAsync"/> loads with <c>-agentpath</c>, or its jar, which it loads with <c>-javaagent</c>.</summary>
/// <param name="FilePath">The path of the agent library or jar.</param>
public sealed record PublishedAgent(string FilePath)
{
    /// <summary>Gets whether this is an agent jar, loaded with <c>-javaagent</c>, rather than a native library.</summary>
    public bool IsJar => FilePath.EndsWith(value: ".jar", StringComparison.OrdinalIgnoreCase);

    /// <summary>Finds a published agent library in <c>artifacts/agent/&lt;rid&gt;</c>, searching upward from the test assembly and the current directory, or in <c>JVMBRIDGE_AGENT_DIRECTORY</c>. The test is skipped when the agent has not been published.</summary>
    /// <param name="name">The agent's assembly name, without extension.</param>
    /// <param name="runtimeIdentifier">The runtime identifier, or <see langword="null"/> for <c>TARGET_RID</c> or the current platform.</param>
    /// <returns>The published agent.</returns>
    public static PublishedAgent Find(string name, string? runtimeIdentifier = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        string identifier = runtimeIdentifier ?? Environment.GetEnvironmentVariable(variable: "TARGET_RID") ?? RuntimeInformation.RuntimeIdentifier;

        if (TryFind(name, identifier, [AppContext.BaseDirectory, Directory.GetCurrentDirectory()]) is { } agent)
            return agent;

        Assert.Skip(MissingMessage(name, identifier));

        throw new InvalidOperationException(message: "Assert.Skip returned.");
    }

    /// <summary>
    /// Finds the jar that a publish with <c>JvmBridgeAgentJar</c> wrote beside the library, or that <c>jvmbridge pack</c> wrote there, in the same places as <see cref="Find"/>. <see cref="Jdk.RunAsync"/> loads it with <c>-javaagent</c>. The test is skipped when there is
    /// no such jar.
    /// </summary>
    /// <param name="name">The agent's assembly name, without extension.</param>
    /// <param name="runtimeIdentifier">The runtime identifier, or <see langword="null"/> for <c>TARGET_RID</c> or the current platform.</param>
    /// <returns>The published agent jar.</returns>
    public static PublishedAgent FindJar(string name, string? runtimeIdentifier = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        string identifier = runtimeIdentifier ?? Environment.GetEnvironmentVariable(variable: "TARGET_RID") ?? RuntimeInformation.RuntimeIdentifier;

        if (TryFind(name + ".jar", identifier, [AppContext.BaseDirectory, Directory.GetCurrentDirectory()]) is { } agent)
            return agent;

        Assert.Skip(MissingJarMessage(name, identifier));

        throw new InvalidOperationException(message: "Assert.Skip returned.");
    }

    internal static string MissingJarMessage(string name, string identifier)
    {
        return $"The {name} agent jar is not published for {identifier}. Publish it with: dotnet publish -c Release -r {identifier} -o artifacts/agent/{identifier} -p:JvmBridgeAgentJar=true";
    }

    internal static string MissingMessage(string name, string identifier)
    {
        return $"The {name} agent is not published for {identifier}. Publish it with: dotnet publish -c Release -r {identifier} -o artifacts/agent/{identifier}";
    }

    internal static PublishedAgent? TryFind(string name, string identifier, IEnumerable<string> starts)
    {
        string file = name.EndsWith(value: ".jar", StringComparison.OrdinalIgnoreCase) ? name : name + (OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so");

        if (Environment.GetEnvironmentVariable(variable: "JVMBRIDGE_AGENT_DIRECTORY") is { Length: > 0 } configured)
        {
            foreach (string candidate in new[] { Path.Combine(configured, file), Path.Combine(configured, identifier, file) })
            {
                if (File.Exists(candidate))
                    return new PublishedAgent(candidate);
            }
        }

        foreach (string start in starts)
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(paths: [directory.FullName, "artifacts", "agent", identifier, file]);

                if (File.Exists(candidate))
                    return new PublishedAgent(candidate);
            }
        }

        return null;
    }
}

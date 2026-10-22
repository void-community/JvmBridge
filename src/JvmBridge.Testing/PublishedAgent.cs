using System.Runtime.InteropServices;

using Xunit;

namespace JvmBridge.Testing;

/// <summary>A NativeAOT agent library that has been published for the current platform.</summary>
/// <param name="FilePath">The path of the agent library.</param>
public sealed record PublishedAgent(string FilePath)
{
    /// <summary>Finds a published agent in <c>artifacts/agent/&lt;rid&gt;</c>, searching upward from the test assembly and the current directory, or in <c>JVMBRIDGE_AGENT_DIRECTORY</c>. The test is skipped when the agent has not been published.</summary>
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

    internal static string MissingMessage(string name, string identifier)
    {
        return $"The {name} agent is not published for {identifier}. Publish it with: dotnet publish -c Release -r {identifier} -o artifacts/agent/{identifier}";
    }

    internal static PublishedAgent? TryFind(string name, string identifier, IEnumerable<string> starts)
    {
        string file = name + (OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so");

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

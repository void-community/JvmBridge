using System.Diagnostics.CodeAnalysis;

using JvmBridge.Launcher;

using Microsoft.Build.Framework;

using BuildTask = Microsoft.Build.Utilities.Task;

namespace JvmBridge.Tasks;

/// <summary>
/// Writes a jar that <c>java -javaagent</c> starts, from native agent libraries and from the libraries of other such jars: the same jar that <c>jvmbridge pack</c> writes. <c>JvmBridgeAgentJar</c> uses it after a publish, and <c>JvmBridgeMergeAgentJars</c> to combine the jars
/// of several platforms.
/// </summary>
public sealed class PackAgentJar : BuildTask
{
    /// <summary>Gets or sets the jars whose libraries go into the new jar.</summary>
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "MSBuild passes an item list to a task as ITaskItem[]."
    )]
    public ITaskItem[] Jars { get; set; } = [];

    /// <summary>Gets or sets the native libraries, each with the <c>Rid</c> metadata of the platform it runs on.</summary>
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "MSBuild passes an item list to a task as ITaskItem[]."
    )]
    public ITaskItem[] Libraries { get; set; } = [];

    /// <summary>Gets or sets the jar to write.</summary>
    [Required]
    public string OutputFile { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override bool Execute()
    {
        List<AgentLibrary> libraries = [];

        try
        {
            foreach (ITaskItem library in Libraries)
            {
                string path = library.GetMetadata(metadataName: "FullPath");
                string rid = library.GetMetadata(metadataName: "Rid");

                if (!File.Exists(path))
                    return Fail($"The agent library for {rid} does not exist: {path}");

                libraries.Add(AgentLibrary.FromFile(rid, path));
            }

            foreach (ITaskItem jar in Jars)
            {
                string path = jar.GetMetadata(metadataName: "FullPath");

                if (!File.Exists(path))
                    return Fail($"The jar does not exist: {path}");

                libraries.AddRange(LauncherJar.Read(path));
            }

            IReadOnlyList<AgentLibrary> combined = LauncherJar.Combine(libraries);
            string launcher = LauncherJar.Write(OutputFile, combined);

            Log.LogMessage(
                MessageImportance.High,
                $"JvmBridge: {OutputFile} (launcher {launcher}) holds the agent for {string.Join(separator: ", ", combined.Select(static library => library.Rid).Order(StringComparer.Ordinal))}."
            );

            return true;
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Fail(failure.Message);
        }
    }

    private bool Fail(string message)
    {
        Log.LogError(
            subcategory: null,
            errorCode: "JVMB014",
            helpKeyword: null,
            file: null,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            "The agent jar could not be written: " + message
        );

        return false;
    }
}

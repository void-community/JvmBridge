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
    /// <value>The full path of each jar on a line of its own, as text because MSBuild hands an item list only to an array; empty, the default, for none.</value>
    public string Jars { get; set; } = string.Empty;

    /// <summary>Gets or sets the native libraries, each with the runtime identifier of the platform it runs on.</summary>
    /// <value>One line for each library: its full path, a tab, and its runtime identifier; empty, the default, for none.</value>
    public string Libraries { get; set; } = string.Empty;

    /// <summary>Gets or sets the jar to write.</summary>
    /// <value>The path of the new jar, which replaces a file that is already there; empty until MSBuild sets it, and required.</value>
    [Required]
    public string OutputFile { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override bool Execute()
    {
        List<AgentLibrary> libraries = [];

        try
        {
            foreach (string library in Lines(Libraries))
            {
                string[] fields = library.Split(separator: '\t');
                string path = fields[0];
                string rid = fields.Length > 1 ? fields[1] : string.Empty;

                if (!File.Exists(path))
                    return Fail(message: $"The agent library for {rid} does not exist: {path}");

                libraries.Add(AgentLibrary.FromFile(rid, path));
            }

            foreach (string path in Lines(Jars))
            {
                if (!File.Exists(path))
                    return Fail(message: $"The jar does not exist: {path}");

                libraries.AddRange(LauncherJar.Read(path));
            }

            IReadOnlyList<AgentLibrary> combined = LauncherJar.Combine(libraries);
            string launcher = LauncherJar.Write(OutputFile, combined);

            Log.LogMessage(MessageImportance.High,
                message: $"JvmBridge: {OutputFile} (launcher {launcher}) holds the agent for {string.Join(separator: ", ", combined.Select(static library => library.Rid).Order(StringComparer.Ordinal))}.");

            return true;
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Fail(failure.Message);
        }
    }

    private static string[] Lines(string items)
    {
        return items.Split(separator: '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private bool Fail(string message)
    {
        Log.LogError(subcategory: null,
            errorCode: "JVMB014",
            helpKeyword: null,
            file: null,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            "The agent jar could not be written: " + message);

        return false;
    }
}

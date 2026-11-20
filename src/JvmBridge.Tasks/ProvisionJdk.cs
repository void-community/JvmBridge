using System.Runtime.InteropServices;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

using Microsoft.Build.Framework;

using Nito.AsyncEx;

using BuildTask = Microsoft.Build.Utilities.Task;

namespace JvmBridge.Tasks;

/// <summary>
/// Provides a JDK to a build that has none: the checksum-pinned archive from JvmBridge's inventory for this machine, downloaded and unpacked once into a cache under the temporary directory that
/// <c>JvmBridge.Testing</c> shares.
/// </summary>
public sealed class ProvisionJdk : BuildTask, ICancelableTask, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Gets or sets the JVM implementation, <c>hotspot</c> or <c>openj9</c>.</summary>
    /// <value>The implementation as the inventory spells it; <c>hotspot</c> unless the build sets another.</value>
    public string Implementation { get; set; } = "hotspot";

    /// <summary>Gets the home directory of the JDK, which contains <c>bin/javac</c>.</summary>
    /// <value>The full path of the unpacked JDK in the cache once the task has succeeded; empty before that and after a failure.</value>
    [Output]
    public string JdkHome { get; private set; } = string.Empty;

    /// <summary>Gets or sets the Java major version to provide.</summary>
    /// <value>A major version that the inventory pins for this machine, such as 17; zero until MSBuild sets it, and required.</value>
    [Required]
    public int Major { get; set; }

    /// <inheritdoc/>
    public void Cancel()
    {
        _cancellation.Cancel();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cancellation.Dispose();
    }

    /// <inheritdoc/>
    public override bool Execute()
    {
        string runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        JdkEntry? entry = PinnedJdk.Find(Major, Implementation, runtimeIdentifier);

        if (entry is not { Status: "available", Sha256.Length: >= 12 })
        {
            string reason = entry?.Reason is { } detail ? " (" + detail + ")" : string.Empty;

            LogFailure(code: "JVMB007",
                $"No pinned Java {Major} {Implementation} JDK exists for {runtimeIdentifier}{reason}. Set JvmBridgeJdkHome, or JAVA_HOME, to a JDK instead.");

            return false;
        }

        try
        {
            Log.LogMessage(MessageImportance.High,
                $"JvmBridge: providing the pinned Java {Major} {Implementation} JDK ({entry.Distribution} {entry.Version}) for {runtimeIdentifier}.");

            // ITask.Execute is synchronous; this is the sole boundary into the asynchronous download.
            JdkHome = AsyncContext.Run(() => PinnedJdk.UnpackAsync(entry, _cancellation.Token));

            return true;
        }
        catch (Exception failure) when (IsExpected(failure))
        {
            LogFailure(code: "JVMB008",
                $"Could not provide the pinned JDK {entry.Url}: {failure.Message} Set JvmBridgeJdkHome, or JAVA_HOME, to an installed JDK to avoid the download.");

            return false;
        }
    }

    private static bool IsExpected(Exception failure)
    {
        return failure is InvalidOperationException or IOException or HttpRequestException or OperationCanceledException or TimeoutException or InvalidDataException or UnauthorizedAccessException;
    }

    private void LogFailure(string code, string message)
    {
        Log.LogError(subcategory: null,
            code,
            helpKeyword: null,
            file: null,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message);
    }
}

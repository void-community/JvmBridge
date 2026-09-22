using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

using Microsoft.Build.Framework;

namespace JvmBridge.Build;

/// <summary>Runs deterministic repository generation and validation within MSBuild.</summary>
public sealed class RepositoryTask : Microsoft.Build.Utilities.Task, ICancelableTask, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Gets or sets the requested repository operation.</summary>
    [Required]
    public string Operation { get; set; } = string.Empty;

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
        try
        {
            // ITask.Execute is synchronous; this is the sole boundary into asynchronous build operations.
            Nito.AsyncEx.AsyncContext.Run(() => ExecuteAsync(_cancellation.Token));

            return true;
        }
        catch (Exception failure) when (IsBuildFailure(failure))
        {
            Log.LogErrorFromException(failure, showStackTrace: true);

            return false;
        }
    }

    private static bool IsBuildFailure(Exception failure)
    {
        return failure is InvalidOperationException or IOException or HttpRequestException or OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException;
    }

    private async System.Threading.Tasks.Task ExecuteAsync(CancellationToken cancellationToken)
    {
        RepositoryContext repository = RepositoryContext.Discover();
        ProcessRunner processes = new(repository);
        CompatibilityReporter reporter = new(repository);

        if (Operation is "Generate" or "VerifyGenerated")
        {
            bool check = Operation == "VerifyGenerated";
            await new BindingGenerator(repository, processes).GenerateAsync(check, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            new AbiTool(repository).GenerateInspector(check);
            reporter.Report(verify: false, check);
            CompatibilityConfiguration configuration = JsonFile.Read<CompatibilityConfiguration>(repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "compatibility.json"]));
            JdkLock inventory = JsonFile.Read<JdkLock>(repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "jdks.lock.json"]));
            ManifestValidator.ValidateManifest(configuration, inventory.Jdks);

            return;
        }

        if (Operation == "VerifyCoverage")
        {
            reporter.Report(verify: true, check: false);

            return;
        }

        if (Operation == "CompileFixtures")
        {
            using HttpService http = new();

            await new FixtureCompiler(repository, new JdkArchiveManager(http), processes).CompileAllAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        throw new InvalidOperationException("Unknown repository operation: " + Operation);
    }
}

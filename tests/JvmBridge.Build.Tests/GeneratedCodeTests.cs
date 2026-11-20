using System.Runtime.InteropServices;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

using Xunit;

namespace JvmBridge.Build.Tests;

/// <summary>The checked-in native declarations and compatibility page, which CI's linux-x64 tests generate again as the VerifyGenerated target does.</summary>
public sealed class GeneratedCodeTests
{
    /// <summary>Gets whether these are CI's linux-x64 tests, where the generator of the tool manifest runs.</summary>
    /// <value><see langword="true"/> if the <c>CI</c> environment variable is <see langword="true"/> and the runtime identifier is <c>linux-x64</c>; otherwise, <see langword="false"/>.</value>
    public static bool Enabled => Environment.GetEnvironmentVariable(variable: "CI") == "true"
        && RuntimeInformation.RuntimeIdentifier == "linux-x64";

    /// <summary>The generator writes the declarations that are checked in, the compatibility page describes the pinned inventory, and the inventory holds every cell of the grid.</summary>
    [Fact(SkipUnless = nameof(Enabled),
        Skip = "Restore the tool manifest and run dotnet build src/JvmBridge.Build -c Release -t:VerifyGenerated.")]
    public async Task TheCheckedInOutputIsWhatGenerationWritesAsync()
    {
        RepositoryContext repository = RepositoryContext.Discover();
        ProcessRunner processes = new(repository);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await processes.RunCheckedAsync(command:
        [
            "dotnet",
            "tool",
            "restore",
            "--tool-manifest",
            repository.PathFromRoot(parts: ["src", "JvmBridge.Build", ".config", "dotnet-tools.json"]),
        ],
            cancellationToken: cancellationToken);

        await new BindingGenerator(repository, processes).GenerateAsync(check: true, cancellationToken);

        new CompatibilityReporter(repository).Report(verify: false, check: true);
        ManifestValidator.ValidateManifest(Compatibility.Configuration, PinnedJdks.Jdks);
    }
}

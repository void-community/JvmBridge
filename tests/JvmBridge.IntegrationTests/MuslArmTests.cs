using System.Runtime.InteropServices;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

using Xunit;

namespace JvmBridge.IntegrationTests;

/// <summary>
/// linux-musl-arm, which has neither a Java to run an agent on nor a runtime of CI's own: CI's linux-x64 tests cross-build HelloAgent for it in the image of .NET's cross-build toolchain that the target names, and
/// the library must export the entry points that a JVM calls.
/// </summary>
public sealed class MuslArmTests
{
    private const string Rid = "linux-musl-arm";

    /// <summary>Gets whether these are CI's linux-x64 tests, which the image runs on.</summary>
    public static bool Enabled => TestTarget.PublishedByCi && RuntimeInformation.RuntimeIdentifier == "linux-x64";

    /// <summary>The agent links against the image's ARM32 musl root file system with the runner's own .NET SDK, and exports its entry points.</summary>
    [Fact(SkipUnless = nameof(Enabled), Skip = "Runs in CI's linux-x64 tests, with the image of .NET's cross-build toolchain.")]
    public async Task TheCrossBuiltAgentExportsItsEntryPointsAsync()
    {
        RepositoryContext repository = RepositoryContext.Discover();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string image = Compatibility.Configuration.Targets.Single(static target => target.Rid == Rid).CrossContainer ?? throw new InvalidOperationException(message: "The target names no image.");
        string sdk = Path.GetDirectoryName(ProcessRunner.FindExecutable(name: "dotnet")) ?? throw new InvalidOperationException(message: "dotnet is not on the path.");

        await new ProcessRunner(repository).RunCheckedAsync(
            [
                "docker", "run", "--rm", "-v", repository.Root + ":/repo", "-v", sdk + ":/dotnet:ro", "-w", "/repo",
                "-e", "DOTNET_ROOT=/dotnet", "-e", "PATH=/dotnet:/usr/local/bin:/usr/bin:/bin", "-e", "Version", image,
                "dotnet", "publish", "samples/HelloAgent", "--configuration", "Release", "--runtime", Rid,
                "--property:SysRoot=/crossrootfs/arm", "--property:LinkerFlavor=lld", "--property:ObjCopyName=llvm-objcopy", "--output", "artifacts/agent/" + Rid
            ],
            Path.Combine(repository.Artifacts, path2: "results", Rid, path4: "publish.log"),
            TimeSpan.FromMinutes(minutes: 20),
            cancellationToken: cancellationToken
        );

        await NativeTests.AssertExportsAsync(repository, Rid, cancellationToken);
    }
}

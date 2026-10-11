using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

using Xunit;

namespace JvmBridge.IntegrationTests;

/// <summary>Checks the packaged native exception handler with actual Windows faults.</summary>
public sealed class WindowsX86CompatibilityTests
{
    /// <summary>Gets whether the Windows x86 package consumers are selected.</summary>
    public static bool Enabled => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable(variable: "TARGET_RID") == "win-x86";

    /// <summary>Recovers only the legacy probe, including concurrent calls and DLL lifetime.</summary>
    [Fact(SkipUnless = nameof(Enabled), Skip = "Requires the published Windows x86 package consumer.")]
    public async Task NativeFaultRecoveryAsync()
    {
        RepositoryContext repository = RepositoryContext.Discover();
        string directory = Path.Combine(repository.Artifacts, path2: "results", path3: "win-x86", path4: "safefetch");
        FileSystem.EnsureDirectory(directory);
        string executable = Path.Combine(directory, path2: "SafeFetchTests.exe");
        string library = Path.Combine(repository.Artifacts, path2: "host", path3: "win-x86", path4: "JvmBridge.WindowsX86.dll");
        Assert.True(File.Exists(library), userMessage: "The NuGet native asset must be copied to the published host.");
        ProcessRunner processes = new(repository);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await processes.RunCheckedAsync(
            ["cl", "/nologo", "/O2", "/W4", "/WX", repository.PathFromRoot(parts: ["tests", "Fixtures", "WindowsX86SafeFetch.c"]), "/Fe:" + executable, "/Fo:" + Path.Combine(directory, path2: "SafeFetchTests.obj")],
            Path.Combine(directory, path2: "compile.log"),
            cancellationToken: cancellationToken
        );
        ProcessResult positive = await processes.RunAsync([executable, library, "recover"], Path.Combine(directory, path2: "recover.log"), cancellationToken: cancellationToken);
        Assert.Contains(expectedSubstring: "SAFEFETCH_RECOVERED=402", positive.Output, StringComparison.Ordinal);

        foreach (string scenario in new[] { "read", "write", "execute", "signature", "caller" })
        {
            ProcessResult negative = await processes.RunAsync(
                [executable, library, scenario],
                Path.Combine(directory, scenario + ".log"),
                expectedExitCode: unchecked((int)0xc0000005),
                cancellationToken: cancellationToken
            );

            Assert.Contains(expectedSubstring: "UNRELATED_FAULT_CONTROL", negative.Output, StringComparison.Ordinal);
        }
    }
}

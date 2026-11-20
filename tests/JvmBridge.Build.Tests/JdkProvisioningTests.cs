using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Build.Tests;

/// <summary>Builds a real consumer against the freshly packed package to verify the opt-in download of the pinned JDK.</summary>
public sealed class JdkProvisioningTests
{
    /// <summary>A Java version without a pinned JDK fails before any download and names the settings that avoid it.</summary>
    [Fact]
    public async Task AJavaVersionWithoutAPinnedJdkIsReportedAsync()
    {
        using TemporaryDirectory project = JavaSourceTests.CreateProject(JavaSourceTests.PackedVersion());

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path,
            jdk: null,
            properties: ["JvmBridgeProvisionJdk=true", "JvmBridgeJdkMajor=7"]);

        Assert.NotEqual(expected: 0, build.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB007", build.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "JvmBridgeJdkHome", build.Output, StringComparison.Ordinal);
    }

    /// <summary>With JvmBridgeProvisionJdk, a machine that has no JDK downloads the pinned one and compiles the Java sources.</summary>
    [Fact]
    public async Task AMachineWithoutAJdkBuildsWhenProvisioningIsEnabledAsync()
    {
        using TemporaryDirectory project = JavaSourceTests.CreateProject(JavaSourceTests.PackedVersion());

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path,
            jdk: null,
            properties: ["JvmBridgeProvisionJdk=true"]);

        Assert.True(build.ExitCode == 0, build.Output);
        Assert.Contains(expectedSubstring: "providing the pinned Java 17", build.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(paths: [project.Path, "obj", "Release", "net10.0", "jvmbridge-java", "classes", "pkg", "Helper.class"])), build.Output);
    }

    /// <summary>A JDK directory named by the project is never replaced by a download, even when it is unusable.</summary>
    [Fact]
    public async Task AnExplicitJdkHomeIsNeverReplacedByADownloadAsync()
    {
        using TemporaryDirectory project = JavaSourceTests.CreateProject(JavaSourceTests.PackedVersion());

        string emptyJdk = Path.Combine(project.Path, path2: "not-a-jdk");

        FileSystem.EnsureDirectory(emptyJdk);

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path,
            emptyJdk,
            properties: ["JvmBridgeProvisionJdk=true"]);

        Assert.NotEqual(expected: 0, build.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB005", build.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "providing the pinned", build.Output, StringComparison.Ordinal);
    }

    /// <summary>Without the opt-in, nothing is downloaded and the missing JDK is reported with the opt-in as one remedy.</summary>
    [Fact]
    public async Task ProvisioningStaysOffByDefaultAsync()
    {
        using TemporaryDirectory project = JavaSourceTests.CreateProject(JavaSourceTests.PackedVersion());

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path, jdk: null);

        Assert.NotEqual(expected: 0, build.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB004", build.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "JvmBridgeProvisionJdk", build.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "providing the pinned", build.Output, StringComparison.Ordinal);
    }
}

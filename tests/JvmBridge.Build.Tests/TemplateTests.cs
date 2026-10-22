using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Build.Tests;

/// <summary>Instantiates the dotnet new template from the freshly packed packages and builds what it generates.</summary>
public sealed class TemplateTests
{
    /// <summary>
    /// The template's source name must never collide with an MSBuild property, and the generated agent and tests build and pass against the packed packages on a machine with no JDK, which the template's JvmBridgeProvisionJdk covers. The JVM tests skip because no agent is published here.
    /// </summary>
    [Fact]
    public async Task GeneratedProjectsKeepTheirPropertiesAndBuildAsync()
    {
        RepositoryContext repository = RepositoryContext.Discover();
        string version = PackedVersion(repository);
        string packages = Path.Combine(repository.Artifacts, path2: "packages");

        using TemporaryDirectory work = new(prefix: "jvmbridge-template-");

        ProcessRunner runner = new(repository);
        string hive = Path.Combine(work.Path, path2: "hive");
        string project = Path.Combine(work.Path, path2: "Demo");

        Dictionary<string, string?> environment = new()
        {
            [key: "NUGET_PACKAGES"] = Path.Combine(work.Path, path2: "packages-cache"),
            [key: "DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            [key: "DOTNET_NOLOGO"] = "1",
            [key: "MSBUILDDISABLENODEREUSE"] = "1",
            [key: "JAVA_HOME"] = null,
            [key: "JvmBridgeJdkHome"] = null,
            [key: "TARGET_RID"] = null
        };

        string template = Path.Combine(packages, "JvmBridge.Templates." + version + ".nupkg");

        await runner.RunCheckedAsync(
            ["dotnet", "new", "install", template, "--debug:custom-hive", hive],
            logPath: null,
            environment: environment,
            workingDirectory: work.Path
,
            cancellationToken: TestContext.Current.CancellationToken
        );
        await runner.RunCheckedAsync(
            ["dotnet", "new", "jvmbridge-agent", "-n", "Demo", "-o", project, "--jvmBridgeVersion", version, "--debug:custom-hive", hive],
            logPath: null,
            environment: environment,
            workingDirectory: work.Path
,
            cancellationToken: TestContext.Current.CancellationToken
        );

        string agent = await File.ReadAllTextAsync(Path.Combine(project, path2: "Demo.csproj"), TestContext.Current.CancellationToken);
        string tests = await File.ReadAllTextAsync(Path.Combine(project, path2: "tests", path3: "Demo.Tests", path4: "Demo.Tests.csproj"), TestContext.Current.CancellationToken);
        string source = await File.ReadAllTextAsync(Path.Combine(project, path2: "PatcherAgent.cs"), TestContext.Current.CancellationToken);

        Assert.Contains(expectedSubstring: "<JvmBridgeAgent>true</JvmBridgeAgent>", agent, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "<PublishAot>true</PublishAot>", agent, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "<JvmBridgeProvisionJdk>true</JvmBridgeProvisionJdk>", agent, StringComparison.Ordinal);
        Assert.Contains("Include=\"JvmBridge\" Version=\"" + version + "\"", agent, StringComparison.Ordinal);
        Assert.Contains("Include=\"JvmBridge.Testing\" Version=\"" + version + "\"", tests, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "MyJvmAgent", string.Concat(agent, tests, source), StringComparison.Ordinal);

        List<string> restore = ["-p:RestoreAdditionalProjectSources=" + packages];

        ProcessResult build = await runner.RunAsync(
            ["dotnet", "build", Path.Combine(project, path2: "tests", path3: "Demo.Tests", path4: "Demo.Tests.csproj"), "-c", "Release", .. restore],
            logPath: null,
            environment: environment,
            workingDirectory: project,
            expectedExitCode: null
,
            cancellationToken: TestContext.Current.CancellationToken
        );

        ProcessResult test = await runner.RunAsync(
            ["dotnet", "test", Path.Combine(project, path2: "tests", path3: "Demo.Tests", path4: "Demo.Tests.csproj"), "-c", "Release", "--no-build"],
            logPath: null,
            environment: environment,
            workingDirectory: project,
            expectedExitCode: null
,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.True(build.ExitCode == 0, build.Output);
        Assert.True(test.ExitCode == 0, test.Output);
        Assert.Contains(expectedSubstring: "Passed:     1, Skipped:     3", test.Output, StringComparison.Ordinal);
    }

    private static string PackedVersion(RepositoryContext repository)
    {
        string packages = Path.Combine(repository.Artifacts, path2: "packages");

        string package = Directory.Exists(packages)
            ? Directory.EnumerateFiles(packages, searchPattern: "JvmBridge.Templates.*.nupkg").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? string.Empty
            : string.Empty;

        return package.Length == 0
            ? throw new InvalidOperationException(
                message: "Pack the packages into artifacts/packages first: dotnet pack src/JvmBridge, src/JvmBridge.Testing and src/JvmBridge.Templates with the same version."
            )
            : Path.GetFileNameWithoutExtension(package)["JvmBridge.Templates.".Length..];
    }
}

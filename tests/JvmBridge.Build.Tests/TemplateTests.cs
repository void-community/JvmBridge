using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Build.Tests;

/// <summary>Instantiates the dotnet new template from the freshly packed packages and builds what it generates.</summary>
public sealed class TemplateTests
{
    /// <summary>
    /// The template's source name must never collide with an MSBuild property, and the generated agent and tests build and pass against the packed packages on a machine with no JDK, which the template's JvmBridgeProvisionJdk covers. The agent is published for this machine, its jar is combined the way the template's release job does it, and the JVM tests run through the library and the jar.
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
        string rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        string published = Path.Combine(project, path2: "artifacts", path3: "agent", rid);

        // The agent is published for this machine, so the JVM tests run instead of skipping: through the library and through the jar that JvmBridgeAgentJar writes beside it.
        ProcessResult publish = await runner.RunAsync(
            ["dotnet", "publish", Path.Combine(project, path2: "Demo.csproj"), "-c", "Release", "-r", rid, "--self-contained", "-o", published, .. restore],
            logPath: null,
            environment: environment,
            workingDirectory: project,
            expectedExitCode: null,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.True(publish.ExitCode == 0, publish.Output);
        Assert.True(File.Exists(Path.Combine(published, path2: "Demo.jar")), publish.Output);

        // The release job of the template combines the jars of the platforms with the package's target; one jar combines into the same jar.
        ProcessResult merge = await runner.RunAsync(
            ["dotnet", "msbuild", Path.Combine(project, path2: "Demo.csproj"), "-restore", "-t:JvmBridgeMergeAgentJars", "-p:JvmBridgeAgentJars=artifacts/agent/*/Demo.jar", "-p:JvmBridgeAgentJarOutput=Demo.jar", .. restore],
            logPath: null,
            environment: environment,
            workingDirectory: project,
            expectedExitCode: null,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.True(merge.ExitCode == 0, merge.Output);
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(published, path2: "Demo.jar"), TestContext.Current.CancellationToken),
            await File.ReadAllBytesAsync(Path.Combine(project, path2: "Demo.jar"), TestContext.Current.CancellationToken)
        );

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
        Assert.Contains(expectedSubstring: "Passed:     7, Skipped:     0", test.Output, StringComparison.Ordinal);
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

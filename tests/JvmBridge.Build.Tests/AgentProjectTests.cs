using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Build.Tests;

/// <summary>Builds a minimal agent project against the freshly packed package to verify the checks on how an agent project is configured.</summary>
public sealed class AgentProjectTests
{
    private const string AgentSource = """
        using JvmBridge.Agents;

        public sealed class ProbeAgent : JavaAgent
        {
        }
        """;

    /// <summary>JvmBridgeMergeAgentJars with nothing to combine says how to name the jars.</summary>
    [Fact]
    public async Task AMergeWithoutJarsIsReportedAsync()
    {
        using TemporaryDirectory project = CreateProject(includePublishAot: true);

        RepositoryContext repository = RepositoryContext.Discover();
        string packages = Path.Combine(repository.Artifacts, path2: "packages");

        Dictionary<string, string?> environment = new()
        {
            [key: "NUGET_PACKAGES"] = Path.Combine(project.Path, path2: "packages-cache"),
            [key: "DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            [key: "DOTNET_NOLOGO"] = "1",
            [key: "MSBUILDDISABLENODEREUSE"] = "1"
        };

        ProcessResult merge = await new ProcessRunner(repository).RunAsync(command:
        [
            "dotnet",
            "msbuild",
            Path.Combine(project.Path, path2: "Consumer.csproj"),
            "-restore",
            "-t:JvmBridgeMergeAgentJars",
            "-p:JvmBridgeAgentJars=agents/*/Probe.jar",
            "-p:RestoreAdditionalProjectSources=" + packages,
        ],
            logPath: null,
            environment: environment,
            expectedExitCode: null,
            workingDirectory: project.Path,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(expected: 0, merge.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB015", merge.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "JvmBridgeAgentJars", merge.Output, StringComparison.Ordinal);
    }

    /// <summary>PublishAot passed on the command line, as the repository's own publish commands do, satisfies the check as well.</summary>
    [Fact]
    public async Task APublishAotCommandLineValueCountsAsync()
    {
        using TemporaryDirectory project = CreateProject(includePublishAot: false);

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path, jdk: null, properties: ["PublishAot=true"]);

        Assert.True(build.ExitCode == 0, build.Output);
    }

    /// <summary>PublishAot in the project satisfies the check.</summary>
    [Fact]
    public async Task AnAgentThatSetsPublishAotBuildsAsync()
    {
        using TemporaryDirectory project = CreateProject(includePublishAot: true);

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path, jdk: null);

        Assert.True(build.ExitCode == 0, build.Output);
        Assert.DoesNotContain(expectedSubstring: "JVMB006", build.Output, StringComparison.Ordinal);
    }

    /// <summary>An agent project that only sets JvmBridgeAgent would publish managed assemblies and no native library, so the build says so.</summary>
    [Fact]
    public async Task AnAgentWithoutPublishAotIsRejectedAsync()
    {
        using TemporaryDirectory project = CreateProject(includePublishAot: false);

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path, jdk: null);

        Assert.NotEqual(expected: 0, build.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB006", build.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "PublishAot", build.Output, StringComparison.Ordinal);
    }

    private static TemporaryDirectory CreateProject(bool includePublishAot)
    {
        TemporaryDirectory project = new(prefix: "jvmbridge-agentproject-");
        string publishAot = includePublishAot ? "<PublishAot>true</PublishAot>" : string.Empty;

        File.WriteAllText(Path.Combine(project.Path, path2: "Agent.cs"), AgentSource);

        File.WriteAllText(Path.Combine(project.Path, path2: "Consumer.csproj"),
            contents: $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <JvmBridgeAgent>true</JvmBridgeAgent>
                {publishAot}
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="JvmBridge" Version="{JavaSourceTests.PackedVersion()}" />
              </ItemGroup>
            </Project>
            """);

        return project;
    }
}

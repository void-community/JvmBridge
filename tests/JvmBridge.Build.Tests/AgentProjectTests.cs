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

    /// <summary>PublishAot passed on the command line, as the repository's own publish commands do, satisfies the check as well.</summary>
    [Fact]
    public async Task APublishAotCommandLineValueCountsAsync()
    {
        using TemporaryDirectory project = CreateProject(includePublishAot: false);

        ProcessResult result = await JavaSourceTests.BuildAsync(project.Path, jdk: null, ["PublishAot=true"]);

        Assert.True(result.ExitCode == 0, result.Output);
    }

    /// <summary>PublishAot in the project satisfies the check.</summary>
    [Fact]
    public async Task AnAgentThatSetsPublishAotBuildsAsync()
    {
        using TemporaryDirectory project = CreateProject(includePublishAot: true);

        ProcessResult result = await JavaSourceTests.BuildAsync(project.Path, jdk: null);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(expectedSubstring: "JVMB006", result.Output, StringComparison.Ordinal);
    }

    /// <summary>An agent project that only sets JvmBridgeAgent would publish managed assemblies and no native library, so the build says so.</summary>
    [Fact]
    public async Task AnAgentWithoutPublishAotIsRejectedAsync()
    {
        using TemporaryDirectory project = CreateProject(includePublishAot: false);

        ProcessResult result = await JavaSourceTests.BuildAsync(project.Path, jdk: null);

        Assert.NotEqual(expected: 0, result.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB006", result.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "PublishAot", result.Output, StringComparison.Ordinal);
    }

    private static TemporaryDirectory CreateProject(bool includePublishAot)
    {
        TemporaryDirectory project = new(prefix: "jvmbridge-agentproject-");
        string publishAot = includePublishAot ? "<PublishAot>true</PublishAot>" : string.Empty;

        File.WriteAllText(Path.Combine(project.Path, path2: "Agent.cs"), AgentSource);

        File.WriteAllText(
            Path.Combine(project.Path, path2: "Consumer.csproj"),
            $"""
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
            """
        );

        return project;
    }
}

using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Build.Tests;

/// <summary>Builds a real consumer against the freshly packed package to verify that JavaSource items become typed definitions.</summary>
public sealed class JavaSourceTests
{
    private const string HelperSource = """
        package pkg;

        public final class Helper {
            private static final Runnable ACTION = new Runnable() { public void run() { } };

            public static int one() { return 1; }
        }
        """;

    private const string ProgramSource = """
        using JvmBridge.Agents;

        Console.WriteLine(string.Join("|", JavaClasses.Pkg.Helper.Name, JavaClasses.Pkg.Helper.NestedClasses.Count.ToString(), Convert.ToHexStringLower(JavaClasses.Pkg.Helper.Bytes.Span[..4].ToArray()), JavaClasses.Other.Tools.Utility.Name, Natives.Definition.Name));

        [JavaClass("pkg/Natives")]
        internal static partial class Natives
        {
            [JavaMethod]
            internal static int Twice(int value) => value * 2;
        }
        """;

    private const string UtilitySource = """
        package other.tools;

        public final class Utility {
            public static String name() { return "utility"; }
        }
        """;

    /// <summary>A build with JavaSource items but no JDK says which setting is missing instead of failing inside the compiler.</summary>
    [Fact]
    public async Task ABuildWithoutAJdkNamesTheMissingSettingAsync()
    {
        using TemporaryDirectory project = CreateProject(PackedVersion());

        ProcessResult result = await BuildAsync(project.Path, jdk: null);

        Assert.NotEqual(expected: 0, result.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB004", result.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "JvmBridgeJdkHome", result.Output, StringComparison.Ordinal);
    }

    /// <summary>A JDK directory without javac is reported by path.</summary>
    [Fact]
    public async Task AJdkWithoutJavacIsReportedByPathAsync()
    {
        using TemporaryDirectory project = CreateProject(PackedVersion());

        string emptyJdk = Path.Combine(project.Path, path2: "not-a-jdk");
        FileSystem.EnsureDirectory(emptyJdk);

        ProcessResult result = await BuildAsync(project.Path, emptyJdk);

        Assert.NotEqual(expected: 0, result.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB005", result.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "javac was not found", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Java sources are compiled with the pinned JDK, embedded with their nested classes, and exposed by package; a C# native helper class gets a definition. The consumer is built and run, not just inspected.
    /// </summary>
    [Fact]
    public async Task JavaSourcesBecomeTypedDefinitionsAsync()
    {
        string runtimeIdentifier = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        RepositoryContext repository = RepositoryContext.Discover();

        JdkEntry entry = JsonFile.Read<JdkLock>(repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "jdks.lock.json"])).Jdks
            .FirstOrDefault(
                candidate => candidate.Rid == runtimeIdentifier && candidate.Java == 17 && candidate.Implementation == "hotspot" && candidate.Status == "available"
            )
            ?? throw new InvalidOperationException("No pinned Java 17 compiler is available for " + runtimeIdentifier + ".");

        using TemporaryDirectory jdk = new(prefix: "jvmbridge-javasource-jdk-");

        using TemporaryDirectory project = CreateProject(PackedVersion());

        using HttpService http = new();

        string home = await new JdkArchiveManager(http).UnpackAsync(FixtureCompiler.ToArchive(entry), jdk.Path, CancellationToken.None);

        ProcessResult build = await BuildAsync(project.Path, home);

        ProcessResult run = await new ProcessRunner(repository).RunAsync(
            ["dotnet", Path.Combine(paths: [project.Path, "bin", "Release", "net10.0", "Consumer.dll"])],
            logPath: null,
            workingDirectory: project.Path
,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(expected: 0, build.ExitCode);
        Assert.Equal(expected: "pkg/Helper|1|cafebabe|other/tools/Utility|pkg/Natives", run.Output.Trim());
    }

    internal static async Task<ProcessResult> BuildAsync(string project, string? jdk, IReadOnlyList<string>? properties = null)
    {
        RepositoryContext repository = RepositoryContext.Discover();
        string packages = Path.Combine(repository.Artifacts, path2: "packages");
        string cache = Path.Combine(project, path2: "packages-cache");

        Dictionary<string, string?> environment = new()
        {
            [key: "JAVA_HOME"] = null,
            [key: "JvmBridgeJdkHome"] = null,
            [key: "NUGET_PACKAGES"] = cache,
            [key: "DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            [key: "DOTNET_NOLOGO"] = "1",
            [key: "MSBUILDDISABLENODEREUSE"] = "1"
        };

        List<string> command = ["dotnet", "build", Path.Combine(project, path2: "Consumer.csproj"), "-c", "Release", "-p:RestoreAdditionalProjectSources=" + packages, "-p:RestoreLockedMode=false"];

        if (jdk is not null)
            command.Add("-p:JvmBridgeJdkHome=" + jdk);

        foreach (string property in properties ?? [])
            command.Add("-p:" + property);

        return await new ProcessRunner(repository).RunAsync(command, logPath: null, environment: environment, expectedExitCode: null, workingDirectory: project).ConfigureAwait(continueOnCapturedContext: false);
    }

    internal static TemporaryDirectory CreateProject(string version)
    {
        TemporaryDirectory project = new(prefix: "jvmbridge-javasource-");

        string java = Path.Combine(project.Path, path2: "java");
        FileSystem.EnsureDirectory(Path.Combine(java, path2: "pkg"));
        FileSystem.EnsureDirectory(Path.Combine(java, path2: "other", path3: "tools"));
        File.WriteAllText(Path.Combine(java, path2: "pkg", path3: "Helper.java"), HelperSource);
        File.WriteAllText(Path.Combine(java, path2: "other", path3: "tools", path4: "Utility.java"), UtilitySource);
        File.WriteAllText(Path.Combine(project.Path, path2: "Program.cs"), ProgramSource);

        File.WriteAllText(
            Path.Combine(project.Path, path2: "Consumer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                <RollForward>LatestMajor</RollForward>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="JvmBridge" Version="{version}" />
                <JavaSource Include="java/**/*.java" />
              </ItemGroup>
            </Project>
            """
        );

        return project;
    }

    internal static string PackedVersion()
    {
        string packages = Path.Combine(RepositoryContext.Discover().Artifacts, path2: "packages");

        string package = Directory.Exists(packages)
            ? Directory.EnumerateFiles(packages, searchPattern: "JvmBridge.*.nupkg").Where(IsMainPackage).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? string.Empty
            : string.Empty;

        return package.Length == 0
            ? throw new InvalidOperationException(message: "Pack the JvmBridge package into artifacts/packages first: dotnet pack src/JvmBridge -c Release -o artifacts/packages.")
            : Path.GetFileNameWithoutExtension(package)["JvmBridge.".Length..];
    }

    private static bool IsMainPackage(string path)
    {
        string name = Path.GetFileName(path);
        bool versioned = name.Length > "JvmBridge.".Length && char.IsDigit(name["JvmBridge.".Length]);

        // JvmBridge.Testing and JvmBridge.Templates are packed beside it, and the symbols package shares its prefix.
        return versioned && name.EndsWith(value: ".nupkg", StringComparison.Ordinal) && !name.EndsWith(value: ".snupkg", StringComparison.Ordinal);
    }
}

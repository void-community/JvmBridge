using System.Globalization;
using System.Runtime.InteropServices;

using JvmBridge.Build;
using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;
using JvmBridge.Launcher;

using Xunit;

namespace JvmBridge.IntegrationTests;

/// <summary>
/// Holds the glibc floor of a linux-x64 or linux-arm64 agent published against the sysroot of .NET's cross-build image (the glibc-sysroot action): the published HelloAgent needs no glibc newer than 2.17, and it loads,
/// through -agentpath and through its jar, in a JVM in the container image that JVMBRIDGE_GLIBC_IMAGE names (CentOS 7, glibc 2.17, in CI).
/// </summary>
public sealed class GlibcFloorTests
{
    private const string ImageVariable = "JVMBRIDGE_GLIBC_IMAGE";

    /// <summary>Gets whether a container image is named and the target is a glibc platform that the sysroot covers.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable(ImageVariable) is { Length: > 0 } && Rid is "linux-x64" or "linux-arm64";

    /// <summary>Gets the Java major versions the agent is run on.</summary>
    public static IEnumerable<TheoryDataRow<int>> Majors => [new(p1: 8), new(p1: 17), new(p1: 21)];

    /// <summary>Gets the lowest glibc that such an agent supports.</summary>
    internal static Version Floor { get; } = new(major: 2, minor: 17);

    private static string Rid => Environment.GetEnvironmentVariable(variable: "TARGET_RID") ?? RuntimeInformation.RuntimeIdentifier;

    /// <summary>
    /// The published agent asks for no glibc symbol version newer than the floor, and every global symbol it needs from another library is bound to a version. A symbol that the sysroot lacks is linked unresolved without an error, and
    /// the library then fails to load; linking .NET 10's arm32 runtime against an old arm32 root file system does that with the 64-bit time functions of glibc 2.34.
    /// </summary>
    [Fact(
        SkipUnless = nameof(Enabled),
        Skip = "Set JVMBRIDGE_GLIBC_IMAGE and publish HelloAgent for linux-x64 or linux-arm64 with the glibc-sysroot properties."
    )]
    public async Task TheAgentNeedsNoNewerGlibcThanTheFloorAsync()
    {
        RepositoryContext repository = RepositoryContext.Discover();
        string agent = Path.Combine(repository.Artifacts, path2: "agent", Rid, path4: "HelloAgent.so");

        ProcessResult symbols = await new ProcessRunner(repository).RunAsync(["readelf", "-V", agent], cancellationToken: TestContext.Current.CancellationToken);

        Version[] versions = [.. RequiredGlibc(symbols.Output)];
        Version newest = versions.Max() ?? new Version(major: 0, minor: 0);

        ProcessResult dynamic = await new ProcessRunner(repository).RunAsync(["readelf", "--dyn-syms", "-W", agent], cancellationToken: TestContext.Current.CancellationToken);
        string[] unbound = [.. UnboundSymbols(dynamic.Output)];

        Assert.Empty(unbound);
        Assert.NotEmpty(versions);
        Assert.True(
            newest <= Floor,
            $"HelloAgent.so needs glibc {newest}, newer than the floor {Floor}. Publish it with the properties of the glibc-sysroot action."
        );
    }

    /// <summary>The agent runs the fixture on the old glibc through -agentpath, and through its jar with -javaagent.</summary>
    /// <param name="major">The Java major version.</param>
    [Theory(
        SkipUnless = nameof(Enabled),
        Skip = "Set JVMBRIDGE_GLIBC_IMAGE and publish HelloAgent for linux-x64 or linux-arm64 with the glibc-sysroot properties."
    )]
    [MemberData(nameof(Majors))]
    public async Task TheAgentRunsOnTheOldGlibcAsync(int major)
    {
        RepositoryContext repository = RepositoryContext.Discover();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string image = Environment.GetEnvironmentVariable(ImageVariable) ?? throw new InvalidOperationException(message: "No image is named.");
        string published = Path.Combine(repository.Artifacts, path2: "agent", Rid);

        JdkEntry entry = JsonFile.Read<JdkLock>(repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "jdks.lock.json"]))
            .Jdks.Single(candidate => candidate.Rid == Rid && candidate.Java == major && candidate.Implementation == "hotspot");

        using HttpService http = new();

        using TemporaryDirectory work = new(prefix: "jvmbridge-glibc-");

        ProcessRunner processes = new(repository);
        JdkArchiveManager archives = new(http);
        string home = await archives.UnpackAsync(FixtureCompiler.ToArchive(entry), Path.Combine(work.Path, path2: "jdk"), cancellationToken);
        string fixture = Path.Combine(work.Path, path2: "classes");
        string precompiled = Path.Combine(repository.Artifacts, path2: "fixtures");
        string jar = Path.Combine(work.Path, path2: "agent", path3: "HelloAgent.jar");

        FileSystem.EnsureDirectory(fixture);

        if (File.Exists(Path.Combine(precompiled, path2: "BridgeFixture.class")))
        {
            foreach (string binary in Directory.EnumerateFiles(precompiled, searchPattern: "*.class"))
                File.Copy(binary, Path.Combine(fixture, Path.GetFileName(binary)), overwrite: true);
        }
        else
        {
            await new FixtureCompiler(repository, archives, processes).CompileAsync(home, fixture, major, cancellationToken);
        }

        string launcher = LauncherJar.Write(jar, [AgentLibrary.FromFile(Rid, Path.Combine(published, path2: "HelloAgent.so"))]);
        List<string> container = ["docker", "run", "--rm", "-v", home + ":/jdk:ro", "-v", published + ":/a:ro", "-v", Path.GetDirectoryName(jar) + ":/j:ro", "-v", fixture + ":/f:ro", image];
        string identifier = $"glibc-{major.ToString(CultureInfo.InvariantCulture)}";

        ProcessResult library = await processes.RunAsync(
            [.. container, "/jdk/bin/java", "-agentpath:/a/HelloAgent.so", "-cp", "/f", "BridgeFixture"],
            Path.Combine(repository.Artifacts, path2: "results", Rid, identifier + "-agentpath.log"),
            cancellationToken: cancellationToken
        );

        ProcessResult launched = await processes.RunAsync(
            [.. container, "/jdk/bin/java", "-javaagent:/j/HelloAgent.jar", "-cp", "/f", "BridgeFixture"],
            Path.Combine(repository.Artifacts, path2: "results", Rid, identifier + "-javaagent.log"),
            cancellationToken: cancellationToken
        );

        foreach (string marker in new[] { "AGENT_OK", "PATCH_OK", "MATCHER_PATCH_OK", "JAVA_BASE_PATCH_OK", "ADVICE_OK", "EVENTS_OK", "INTEROP_OK", "UNLOAD" })
            Assert.True(library.Output.Contains(marker, StringComparison.Ordinal), $"Missing {marker} with -agentpath in {image}:\n{library.Output}");

        foreach (string marker in new[] { "AGENT_OK", "ATTACH", "PATCH_OK", "ADVICE_OK", "UNLOAD" })
        {
            Assert.True(
                launched.Output.Contains(marker, StringComparison.Ordinal),
                $"Missing {marker} with -javaagent ({launcher}) in {image}:\n{launched.Output}"
            );
        }
    }

    /// <summary>Reads the glibc symbol versions that the version-needs section of readelf -V lists.</summary>
    internal static IEnumerable<Version> RequiredGlibc(string readelf)
    {
        foreach (string token in readelf.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith(value: "GLIBC_", StringComparison.Ordinal) && Version.TryParse(token["GLIBC_".Length..], out Version? version))
                yield return version;
        }
    }

    /// <summary>Reads the global symbols that readelf --dyn-syms lists as undefined and bound to no version.</summary>
    internal static IEnumerable<string> UnboundSymbols(string readelf)
    {
        foreach (string line in readelf.Split(separator: '\n'))
        {
            string[] columns = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            bool undefinedGlobal = columns.Length >= 8 && columns[4] == "GLOBAL" && columns[6] == "UND";

            if (undefinedGlobal && !columns[7].Contains(value: '@', StringComparison.Ordinal))
                yield return columns[7];
        }
    }
}

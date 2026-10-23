using System.Globalization;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;
using JvmBridge.Launcher;

using Xunit;

namespace JvmBridge.IntegrationTests;

/// <summary>
/// Runs samples/LegacyAgent, an agent built with JavaRelease 6, on Java 6, 7, 8 and 21 from one build, through -agentpath and through its jar with -javaagent. When JVMBRIDGE_LEGACY_GLIBC_IMAGE names a container image (CentOS 6,
/// glibc 2.12, in CI), the agent was published against the glibc 2.12 sysroot: it must need no newer glibc, and it also runs on Java 6 and 7 in that image.
/// </summary>
public sealed class LegacyJavaTests
{
    private const string ImageVariable = "JVMBRIDGE_LEGACY_GLIBC_IMAGE";

    private const string Rid = "linux-x64";

    private const string Skip = "Publish samples/LegacyAgent for linux-x64 to artifacts/legacy-agent/linux-x64 and set TARGET_RID=linux-x64.";

    /// <summary>Gets whether the target is linux-x64, the one platform with pinned Java 6 and 7 JDKs that CI runs, and the sample is published for it.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable(variable: "TARGET_RID") == Rid && File.Exists(AgentPath(RepositoryContext.Discover()));

    /// <summary>Gets whether the agent was published against the glibc 2.12 sysroot and an image to run it in is named.</summary>
    public static bool ImageEnabled => Enabled && Environment.GetEnvironmentVariable(ImageVariable) is { Length: > 0 };

    /// <summary>Gets the Java major versions the agent runs on.</summary>
    public static IEnumerable<TheoryDataRow<int>> Majors => [new(p1: 6), new(p1: 7), new(p1: 8), new(p1: 21)];

    /// <summary>The agent published against the glibc 2.12 sysroot needs no newer glibc and binds every symbol it takes from another library to a version.</summary>
    [Fact(
        SkipUnless = nameof(ImageEnabled),
        Skip = Skip + " Set JVMBRIDGE_LEGACY_GLIBC_IMAGE after publishing it with the glibc 2.12 properties of the glibc-sysroot action."
    )]
    public async Task TheAgentNeedsNoNewerGlibcThan212Async()
    {
        RepositoryContext repository = RepositoryContext.Discover();
        string agent = AgentPath(repository);

        ProcessResult symbols = await new ProcessRunner(repository).RunAsync(["readelf", "-V", agent], cancellationToken: TestContext.Current.CancellationToken);

        ProcessResult dynamic = await new ProcessRunner(repository).RunAsync(["readelf", "--dyn-syms", "-W", agent], cancellationToken: TestContext.Current.CancellationToken);

        Version newest = GlibcFloorTests.RequiredGlibc(symbols.Output).Max() ?? new Version(major: 0, minor: 0);

        Assert.Empty(GlibcFloorTests.UnboundSymbols(dynamic.Output));
        Assert.True(newest <= new Version(major: 2, minor: 12), $"LegacyAgent.so needs glibc {newest}, newer than 2.12.");
    }

    /// <summary>The agent runs the fixture, compiled for Java 6, on each Java through both routes, with every class verified on the -agentpath route; on Java 6 and 7 it also runs in the old-glibc image.</summary>
    /// <param name="major">The Java major version.</param>
    [Theory(SkipUnless = nameof(Enabled), Skip = Skip)]
    [MemberData(nameof(Majors))]
    public async Task TheAgentRunsOnEveryJavaAsync(int major)
    {
        RepositoryContext repository = RepositoryContext.Discover();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ProcessRunner processes = new(repository);
        string home = await PinnedJdk.UnpackAsync(Pinned(repository, major), cancellationToken);
        string fixture = await CompileFixtureAsync(repository, processes, cancellationToken);
        string agent = AgentPath(repository);

        using TemporaryDirectory work = new(prefix: "jvmbridge-legacy-");

        string jar = Path.Combine(work.Path, path2: "LegacyAgent.jar");
        string launcher = LauncherJar.Write(jar, [AgentLibrary.FromFile(Rid, agent)]);
        string java = Path.Combine(home, path2: "bin", path3: "java");
        string identifier = "legacy-" + major.ToString(CultureInfo.InvariantCulture);
        string expectedVersion = major < 9 ? "JAVA 1." + major.ToString(CultureInfo.InvariantCulture) : "JAVA " + major.ToString(CultureInfo.InvariantCulture);

        ProcessResult library = await processes.RunAsync(
            [java, "-Xverify:all", "-agentpath:" + agent, "-cp", fixture, "legacy.LegacyFixture"],
            LogPath(repository, identifier + "-agentpath.log"),
            cancellationToken: cancellationToken
        );

        ProcessResult launched = await processes.RunAsync(
            [java, "-javaagent:" + jar, "-cp", fixture, "legacy.LegacyFixture"],
            LogPath(repository, identifier + "-javaagent.log"),
            cancellationToken: cancellationToken
        );

        string javaMajor = " java=" + major.ToString(CultureInfo.InvariantCulture);
        AssertRan(library.Output, expectedVersion, route: "-agentpath", "VM_INIT" + javaMajor);
        AssertRan(launched.Output, expectedVersion, "-javaagent (" + launcher + ")", "ATTACH" + javaMajor);

        if (!ImageEnabled || major > 7)
            return;

        string image = Environment.GetEnvironmentVariable(ImageVariable) ?? throw new InvalidOperationException(message: "No image is named.");
        List<string> container = ["docker", "run", "--rm", "-v", home + ":/jdk:ro", "-v", Path.GetDirectoryName(agent) + ":/a:ro", "-v", work.Path + ":/j:ro", "-v", fixture + ":/f:ro", image];

        ProcessResult oldLibrary = await processes.RunAsync(
            [.. container, "/jdk/bin/java", "-agentpath:/a/LegacyAgent.so", "-cp", "/f", "legacy.LegacyFixture"],
            LogPath(repository, identifier + "-image-agentpath.log"),
            cancellationToken: cancellationToken
        );

        ProcessResult oldLaunched = await processes.RunAsync(
            [.. container, "/jdk/bin/java", "-javaagent:/j/LegacyAgent.jar", "-cp", "/f", "legacy.LegacyFixture"],
            LogPath(repository, identifier + "-image-javaagent.log"),
            cancellationToken: cancellationToken
        );

        AssertRan(oldLibrary.Output, expectedVersion, "-agentpath in " + image, "VM_INIT" + javaMajor);
        AssertRan(oldLaunched.Output, expectedVersion, "-javaagent in " + image, "ATTACH" + javaMajor);
    }

    private static string AgentPath(RepositoryContext repository)
    {
        return Path.Combine(repository.Artifacts, path2: "legacy-agent", Rid, path4: "LegacyAgent.so");
    }

    private static void AssertRan(string output, string expectedVersion, string route, string start)
    {
        foreach (string marker in new[] { expectedVersion, start, "PATCH_OK", "HELPER_OK", "ADVICE_OK", "TYPED_OK", "CONSTRUCTION_OK", "FACTORY_OK", "REMOVAL_OK", "EVENTS_OK", "UNLOAD" })
            Assert.True(output.Contains(marker, StringComparison.Ordinal), $"Missing {marker} with {route}:\n{output}");
    }

    // The fixture is compiled for Java 6 once, with the pinned Java 8, whose javac still targets it.
    private static async Task<string> CompileFixtureAsync(RepositoryContext repository, ProcessRunner processes, CancellationToken cancellationToken)
    {
        string output = Path.Combine(repository.Artifacts, path2: "legacy-fixture");

        if (File.Exists(Path.Combine(output, path2: "legacy", path3: "LegacyFixture.class")))
            return output;

        string java8 = await PinnedJdk.UnpackAsync(Pinned(repository, major: 8), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        string compiler = Path.Combine(java8, path2: "bin", path3: "javac");
        string staging = output + ".staging-" + Guid.NewGuid().ToString(format: "N");
        FileSystem.EnsureDirectory(staging);

        await processes.RunCheckedAsync(
            [compiler, "-source", "6", "-target", "6", "-Xlint:-options", "-d", staging, repository.PathFromRoot(parts: ["tests", "Fixtures", "LegacyFixture.java"])],
            Path.Combine(staging, path2: "javac.log"),
            cancellationToken: cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        try
        {
            Directory.Move(staging, output);
        }
        catch (IOException) when (Directory.Exists(output))
        {
            // Another test compiled it first.
            Directory.Delete(staging, recursive: true);
        }

        return output;
    }

    private static string LogPath(RepositoryContext repository, string name)
    {
        return Path.Combine(repository.Artifacts, path2: "results", path3: "legacy", name);
    }

    // Java 6 and 7 are Zulu builds from the legacy inventory; the others are the main inventory's.
    private static JdkEntry Pinned(RepositoryContext repository, int major)
    {
        string inventory = major < 8 ? "legacy-jdks.lock.json" : "jdks.lock.json";

        return JsonFile.Read<JdkLock>(repository.PathFromRoot(parts: ["src", "JvmBridge.Build", inventory]))
            .Jdks.Single(entry => entry.Rid == Rid && entry.Java == major && entry.Implementation == "hotspot" && entry.Status == "available");
    }
}

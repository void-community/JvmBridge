using System.Globalization;
using System.Text.Json.Nodes;

using JvmBridge.Build;

using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.IntegrationTests;

/// <summary>Exercises packed NativeAOT consumers against individual JVM inventory cells.</summary>
public sealed class NativeTests
{
    /// <summary>Gets independently reported JVM cells for the selected target.</summary>
    /// <value>
    /// One row of runtime identifier, Java major and JVM implementation per cell. With JVM_JDK_HOME set it is the single cell of that local JDK, taken as Java 25 on HotSpot unless JVM_JAVA_MAJOR and JVM_IMPLEMENTATION say otherwise;
    /// with only JVM_JAVA_MAJOR set it is the one available pinned JDK of that major; with neither, every pinned JDK of the target, where an unavailable one is skipped with the inventory's reason.
    /// </value>
    public static IEnumerable<TheoryDataRow<string, int, string>> Cells
    {
        get
        {
            string rid = TestTarget.Rid;

            if (Environment.GetEnvironmentVariable(variable: "JVM_JDK_HOME") is not null)
            {
                int major = int.Parse(Environment.GetEnvironmentVariable(variable: "JVM_JAVA_MAJOR") ?? "25", CultureInfo.InvariantCulture);

                yield return new TheoryDataRow<string, int, string>(rid,
                    major,
                    Environment.GetEnvironmentVariable(variable: "JVM_IMPLEMENTATION") ?? "hotspot");

                yield break;
            }

            RepositoryContext repository = RepositoryContext.Discover();

            if (Environment.GetEnvironmentVariable(variable: "JVM_JAVA_MAJOR") is { } selectedMajor)
            {
                int major = int.Parse(selectedMajor, CultureInfo.InvariantCulture);
                string implementation = Environment.GetEnvironmentVariable(variable: "JVM_IMPLEMENTATION") ?? "hotspot";

                JdkEntry entry = PinnedJdks.Jdks
                    .Single(entry => entry.Rid == rid && entry.Java == major && entry.Implementation == implementation && entry.Status == "available");

                yield return new TheoryDataRow<string, int, string>(entry.Rid, entry.Java, entry.Implementation);

                yield break;
            }

            foreach (JdkEntry entry in PinnedJdks.Jdks.Where(entry => entry.Rid == rid))
            {
                yield return new TheoryDataRow<string, int, string>(entry.Rid, entry.Java, entry.Implementation)
                {
                    Skip = entry.Status == "available" ? null : entry.Reason
                };
            }
        }
    }

    /// <summary>Gets whether packed native consumers are selected for execution.</summary>
    /// <value><see langword="true"/> if TARGET_RID names a target or CI published the samples for this runtime; otherwise, <see langword="false"/>.</value>
    public static bool NativeEnabled => TestTarget.Selected;

    /// <summary>Verifies required exports without loading the native agent library.</summary>
    [Fact(SkipUnless = nameof(NativeEnabled), Skip = "Set TARGET_RID and publish both samples with dotnet publish as described in README.")]
    public async Task ExportsAsync()
    {
        await AssertExportsAsync(RepositoryContext.Discover(), TestTarget.Rid, TestContext.Current.CancellationToken);
    }

    /// <summary>Checks ABI, startup, attach, hosting, Unicode, references, transformations, and failure containment.</summary>
    /// <param name="rid">Native consumer target.</param>
    /// <param name="major">Java major version.</param>
    /// <param name="implementation">JVM implementation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rid"/> is <see langword="null"/>.</exception>
    [Theory(SkipUnless = nameof(NativeEnabled), Skip = "Set TARGET_RID and publish both samples with dotnet publish as described in README.")]
    [MemberData(nameof(Cells))]
    public async Task JvmContractAsync(string rid, int major, string implementation)
    {
        ArgumentNullException.ThrowIfNull(rid);

        RepositoryContext repository = RepositoryContext.Discover();
        string? localHome = Environment.GetEnvironmentVariable(variable: "JVM_JDK_HOME");

        JdkEntry entry = localHome is null
            ? PinnedJdks.Jdks.Single(entry => entry.Rid == rid && entry.Java == major && entry.Implementation == implementation)
            : new JdkEntry
            {
                Rid = rid,
                Java = major,
                Implementation = implementation,
                Distribution = "local",
                Status = "available"
            };

        string resultPath = Path.Combine(paths: [repository.Artifacts, "results", rid, $"{implementation}-{major}-{entry.Distribution}", "result.json"]);

        using HttpService http = new();

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-integration-");

        ProcessRunner processes = new(repository);
        JdkArchiveManager archives = new(http);

        JvmScenario scenario = new(repository,
            processes,
            new FixtureCompiler(repository, archives, processes),
            new NativeAbiVerifier(repository));

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;

            string home = localHome
                ?? await archives.UnpackAsync(FixtureCompiler.ToArchive(entry), temporary.Path, cancellationToken);

            string compiler = Environment.GetEnvironmentVariable(variable: "NATIVE_COMPILER")
                ?? (OperatingSystem.IsWindows() ? "cl" : "cc");

            NativeToolchain.Ensure();

            // OpenJ9 18 to 22 mark their libj9vm29.so as needing an executable stack, which glibc from 2.41 no longer grants to a library that is loaded after the start. The tunable gives every process the tests
            // start an executable stack from its start, as such a process had it before; an older glibc and musl ignore it.
            bool tunable = OperatingSystem.IsLinux()
                && implementation == "openj9"
                && Environment.GetEnvironmentVariable(variable: "GLIBC_TUNABLES") is null;

            if (tunable)
                Environment.SetEnvironmentVariable(variable: "GLIBC_TUNABLES", value: "glibc.rtld.execstack=2");

            JsonObject report = await scenario.ExerciseAsync(Path.GetFullPath(home), entry, rid, compiler, cancellationToken);

            JsonFile.WriteNode(resultPath, report);
        }
        catch (Exception failure)
        {
            JsonObject failureReport = JsonFile.ToObject(entry);

            failureReport.SetProperty(propertyName: "status", value: "failed");
            failureReport.SetProperty(propertyName: "error", failure.ToString());
            JsonFile.WriteNode(resultPath, failureReport);

            throw;
        }
    }

    /// <summary>Checks that the published HelloAgent of a target exports the entry points that a JVM calls, and records that the target was built.</summary>
    /// <param name="repository">The repository whose artifacts hold the agent.</param>
    /// <param name="rid">The target's runtime identifier.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    internal static async Task AssertExportsAsync(RepositoryContext repository, string rid, CancellationToken cancellationToken)
    {
        string extension = rid.StartsWith(value: "win-", StringComparison.Ordinal)
            ? ".dll"
            : rid.StartsWith(value: "osx-", StringComparison.Ordinal) ? ".dylib" : ".so";

        string agent = Path.Combine(repository.Artifacts, path2: "agent", rid, "HelloAgent" + extension);

        NativeToolchain.Ensure();

        string symbolTool = ProcessRunner.FindExecutable(name: "llvm-nm") ?? ProcessRunner.FindExecutable(name: "nm") ?? "nm";

        string[] command = OperatingSystem.IsWindows()
            ? ["dumpbin", "/exports", agent]
            : [symbolTool, OperatingSystem.IsMacOS() ? "-g" : "-D", agent];

        ProcessResult exports = await new ProcessRunner(repository).RunAsync(command, Path.Combine(repository.Artifacts, path2: "results", rid, path4: "exports.log"), cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        foreach (string symbol in new[] { "Agent_OnLoad", "Agent_OnAttach", "Agent_OnUnload", "JNI_OnLoad" })
            Assert.Contains(symbol, exports.Output, StringComparison.Ordinal);

        JsonFile.Write(Path.Combine(repository.Artifacts, path2: "results", rid, path4: "build-summary.json"),
            new BuildSummary
            {
                Rid = rid
            });
    }
}

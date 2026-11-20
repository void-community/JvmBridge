using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using JvmBridge.Build;

using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.IntegrationTests;

/// <summary>
/// Measures what loading a minimal agent costs: the size of its library, the added start-up time of <c>java -version</c> and, on Linux, the added peak resident memory. Launches with and without the agent alternate, so drift on a busy runner hits both
/// equally, and medians absorb outliers. The numbers are reported, not checked against limits.
/// </summary>
public sealed partial class FootprintTests
{
    private const int MemoryRuns = 5;
    private const int StartupRuns = 20;
    private const int Warmups = 3;

    private const string ProbeSource = """
        import java.nio.file.Files;
        import java.nio.file.Path;
        import java.nio.file.Paths;

        public final class FootprintProbe {
            public static void main(String[] arguments) throws Exception {
                long kilobytes = -1;
                Path status = Paths.get("/proc/self/status");
                if (Files.exists(status)) {
                    for (String line : Files.readAllLines(status)) {
                        if (line.startsWith("VmHWM:")) kilobytes = Long.parseLong(line.replaceAll("[^0-9]", ""));
                    }
                }
                System.out.println("PEAK_RSS_KB=" + kilobytes);
            }
        }
        """;

    private static readonly int[] Majors = [8, 17, 21];

    /// <summary>Gets the Java majors to measure on the selected target, skipping those that have no pinned HotSpot JDK for it.</summary>
    /// <value>One row for each of Java 8, 17 and 21, holding the selected target's runtime identifier and the major. A row whose major has no available pinned HotSpot JDK for the target carries the inventory's reason as its skip message.</value>
    public static IEnumerable<TheoryDataRow<string, int>> Cells
    {
        get
        {
            string rid = TestTarget.Rid;

            foreach (int major in Majors)
            {
                JdkEntry? entry = PinnedJdks.Jdks
                    .FirstOrDefault(candidate => candidate.Rid == rid && candidate.Java == major && candidate.Implementation == "hotspot");

                yield return new TheoryDataRow<string, int>(rid, major)
                {
                    Skip = entry?.Status == "available" ? null : entry?.Reason ?? "No pinned HotSpot JDK for this target."
                };
            }
        }
    }

    /// <summary>Gets whether footprint measurement was requested for a published target, or CI published the agent and the run has a job summary to report the figures in.</summary>
    /// <value><see langword="true"/> if JVMBRIDGE_FOOTPRINT is 1 and TARGET_RID is set, or CI published the samples for this runtime and GITHUB_STEP_SUMMARY names a file; otherwise, <see langword="false"/>.</value>
    public static bool FootprintEnabled =>
        (Environment.GetEnvironmentVariable(variable: "JVMBRIDGE_FOOTPRINT") == "1"
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable: "TARGET_RID")))
            || (TestTarget.PublishedByCi
                && Environment.GetEnvironmentVariable(variable: "GITHUB_STEP_SUMMARY") is { Length: > 0 });

    /// <summary>Measures one Java major on one target and writes the result under <c>artifacts/footprint</c>.</summary>
    /// <param name="rid">The runtime identifier of the published agent.</param>
    /// <param name="major">The Java major version.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rid"/> is <see langword="null"/>.</exception>
    [Theory(SkipUnless = nameof(FootprintEnabled),
        Skip = "Set JVMBRIDGE_FOOTPRINT=1 and TARGET_RID, and publish samples/FootprintAgent to artifacts/footprint-agent/<rid>.")]
    [MemberData(nameof(Cells))]
    public async Task MeasureAsync(string rid, int major)
    {
        ArgumentNullException.ThrowIfNull(rid);

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RepositoryContext repository = RepositoryContext.Discover();

        string extension = rid.StartsWith(value: "win-", StringComparison.Ordinal)
            ? ".dll"
            : rid.StartsWith(value: "osx-", StringComparison.Ordinal) ? ".dylib" : ".so";

        string agent = Path.Combine(repository.Artifacts, path2: "footprint-agent", rid, "FootprintAgent" + extension);

        Assert.True(File.Exists(agent), "Publish samples/FootprintAgent for " + rid + " to " + agent);

        JdkEntry entry = PinnedJdks.Jdks
            .Single(candidate => candidate.Rid == rid && candidate.Java == major && candidate.Implementation == "hotspot" && candidate.Status == "available");

        using HttpService http = new();

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-footprint-");

        ProcessRunner processes = new(repository);
        string? installed = Environment.GetEnvironmentVariable("JVMBRIDGE_JDK_" + major.ToString(CultureInfo.InvariantCulture));

        string home = !string.IsNullOrEmpty(installed) && Directory.Exists(installed)
            ? installed
            : await new JdkArchiveManager(http).UnpackAsync(FixtureCompiler.ToArchive(entry), temporary.Path, cancellationToken);

        string java = Path.Combine(home, path2: "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        string javac = Path.Combine(home, path2: "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac");
        string probes = Path.Combine(temporary.Path, path2: "probe");

        FileSystem.EnsureDirectory(probes);

        string source = Path.Combine(probes, path2: "FootprintProbe.java");

        await File.WriteAllTextAsync(source, ProbeSource, cancellationToken);

        await processes.RunCheckedAsync(command: [javac, "-d", probes, source],
            logPath: null,
            cancellationToken: cancellationToken);

        string[] withoutAgent = [java, "-version"];
        string[] withAgent = [java, "-agentpath:" + agent, "-version"];

        for (int warmup = 0; warmup < Warmups; warmup++)
        {
            await processes.RunCheckedAsync(withoutAgent, logPath: null, cancellationToken: cancellationToken);
            await processes.RunCheckedAsync(withAgent, logPath: null, cancellationToken: cancellationToken);
        }

        List<double> withoutTimes = [];
        List<double> withTimes = [];

        for (int run = 0; run < StartupRuns; run++)
        {
            withoutTimes.Add(await TimeAsync(processes, withoutAgent, cancellationToken));
            withTimes.Add(await TimeAsync(processes, withAgent, cancellationToken));
        }

        long withoutMemory = await PeakMemoryAsync(processes,
            command: [java, "-cp", probes, "FootprintProbe"],
            cancellationToken);

        long withMemory = await PeakMemoryAsync(processes,
            command: [java, "-agentpath:" + agent, "-cp", probes, "FootprintProbe"],
            cancellationToken);

        // The library's size and the unrounded medians are the measured values; the added time below is the difference of the medians before rounding.
        long librarySize = new FileInfo(agent).Length;
        double withoutMedian = Median(withoutTimes);
        double withMedian = Median(withTimes);

        // The footprint report prints timings with one decimal, so they are rounded to that here.
        double startupWithout = Math.Round(withoutMedian, digits: 1);
        double startupWith = Math.Round(withMedian, digits: 1);
        double startupAdded = Math.Round(withMedian - withoutMedian, digits: 1);
        JsonObject measurement = [];

        // The cell that was measured, and the size of the agent library on disk.
        measurement.SetProperty(propertyName: "rid", rid);
        measurement.SetProperty(propertyName: "java", major);
        measurement.SetProperty(propertyName: "librarySizeBytes", librarySize);

        // The median time of java -version without and with the agent, and what the agent adds.
        measurement.SetProperty(propertyName: "startupWithoutAgentMs", startupWithout);
        measurement.SetProperty(propertyName: "startupWithAgentMs", startupWith);
        measurement.SetProperty(propertyName: "startupAddedMs", startupAdded);

        // The peak resident memory of the probe, which is -1 where the system does not report it, and how many launches each start-up median is over.
        measurement.SetProperty(propertyName: "peakMemoryWithoutAgentKb", withoutMemory);
        measurement.SetProperty(propertyName: "peakMemoryWithAgentKb", withMemory);
        measurement.SetProperty(propertyName: "startupRuns", StartupRuns);

        // The file is what the published footprint table is built from; the summary line shows the same figures on the console and in the job summary.
        JsonFile.WriteNode(Path.Combine(paths: [repository.Artifacts, "footprint", rid, $"hotspot-{major.ToString(CultureInfo.InvariantCulture)}.json"]), measurement);
        Summarize(rid, major, librarySize, measured: [startupWithout, startupWith, startupAdded, withoutMemory, withMemory]);
    }

    private static double Median(List<double> values)
    {
        double[] sorted = [.. values.Order()];
        int middle = sorted.Length / 2;

        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static async Task<long> PeakMemoryAsync(ProcessRunner processes, string[] command, CancellationToken cancellationToken)
    {
        List<long> samples = [];

        for (int run = 0; run < MemoryRuns; run++)
        {
            ProcessResult probe = await processes.RunAsync(command, logPath: null, cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            Match match = PeakMemoryLine().Match(probe.Output);

            if (match.Success)
                samples.Add(long.Parse(match.Groups[groupnum: 1].Value, CultureInfo.InvariantCulture));
        }

        return samples.Count == 0
            ? -1
            : long.CreateTruncating(Median(values: [.. samples.Select(static sample => double.CreateChecked(sample))]));
    }

    [GeneratedRegex(@"PEAK_RSS_KB=(-?\d+)")]
    private static partial Regex PeakMemoryLine();

    private static void Summarize(string rid, int major, long librarySize, double[] measured)
    {
        string[] cells =
        [
            rid,
            "Java " + major.ToString(CultureInfo.InvariantCulture),
            librarySize.ToString(CultureInfo.InvariantCulture),
            .. measured.Select(static value => value.ToString(CultureInfo.InvariantCulture)),
        ];

        string line = "| " + string.Join(separator: " | ", cells) + " |";

        Console.WriteLine(line);

        if (Environment.GetEnvironmentVariable(variable: "GITHUB_STEP_SUMMARY") is not { Length: > 0 } summary)
            return;

        if (!File.Exists(summary) || new FileInfo(summary).Length == 0)
        {
            File.AppendAllText(summary, "| Target | Java | Library bytes | Start-up without (ms) | Start-up with (ms) | Added (ms) | Peak memory without (KB) | Peak memory with (KB) |"
                    + Environment.NewLine
                    + "|---|---|---|---|---|---|---|---|"
                    + Environment.NewLine);
        }

        File.AppendAllText(summary, line + Environment.NewLine);
    }

    private static async Task<double> TimeAsync(ProcessRunner processes, string[] command, CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();

        await processes.RunCheckedAsync(command, logPath: null, cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}

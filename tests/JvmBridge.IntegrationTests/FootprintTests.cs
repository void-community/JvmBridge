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
/// equally, and medians absorb outliers. The size is held to the checked-in baseline; time and memory are reported because shared runners are too noisy to fail on them.
/// </summary>
public sealed partial class FootprintTests
{
    private const int MemoryRuns = 5;
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
    private const double SizeBudget = 1.10;
    private const int StartupRuns = 20;

    private const int Warmups = 3;

    private static readonly int[] Majors = [8, 17, 21];

    /// <summary>Gets the Java majors to measure on the selected target, skipping those that have no pinned HotSpot JDK for it.</summary>
    public static IEnumerable<TheoryDataRow<string, int>> Cells
    {
        get
        {
            string rid = Environment.GetEnvironmentVariable(variable: "TARGET_RID") ?? System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
            JdkLock inventory = JsonFile.Read<JdkLock>(RepositoryContext.Discover().PathFromRoot(parts: ["src", "JvmBridge.Build", "jdks.lock.json"]));

            foreach (int major in Majors)
            {
                JdkEntry? entry = inventory.Jdks.FirstOrDefault(candidate => candidate.Rid == rid && candidate.Java == major && candidate.Implementation == "hotspot");

                yield return new TheoryDataRow<string, int>(rid, major) { Skip = entry?.Status == "available" ? null : entry?.Reason ?? "No pinned HotSpot JDK for this target." };
            }
        }
    }

    /// <summary>Gets whether footprint measurement was requested for a published target.</summary>
    public static bool FootprintEnabled => Environment.GetEnvironmentVariable(variable: "JVMBRIDGE_FOOTPRINT") == "1" && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable: "TARGET_RID"));

    /// <summary>Measures one Java major on one target and writes the result under <c>artifacts/footprint</c>.</summary>
    /// <param name="rid">The runtime identifier of the published agent.</param>
    /// <param name="major">The Java major version.</param>
    [Theory(
        SkipUnless = nameof(FootprintEnabled),
        Skip = "Set JVMBRIDGE_FOOTPRINT=1 and TARGET_RID, and publish samples/FootprintAgent to artifacts/footprint-agent/<rid>."
    )]
    [MemberData(nameof(Cells))]
    public async Task MeasureAsync(string rid, int major)
    {
        ArgumentNullException.ThrowIfNull(rid);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RepositoryContext repository = RepositoryContext.Discover();
        string extension = rid.StartsWith(value: "win-", StringComparison.Ordinal) ? ".dll" : rid.StartsWith(value: "osx-", StringComparison.Ordinal) ? ".dylib" : ".so";
        string agent = Path.Combine(repository.Artifacts, path2: "footprint-agent", rid, "FootprintAgent" + extension);
        Assert.True(File.Exists(agent), "Publish samples/FootprintAgent for " + rid + " to " + agent);

        JdkEntry entry = JsonFile.Read<JdkLock>(repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "jdks.lock.json"]))
            .Jdks.Single(
                candidate => candidate.Rid == rid && candidate.Java == major && candidate.Implementation == "hotspot" && candidate.Status == "available"
            );

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
        await processes.RunCheckedAsync([javac, "-d", probes, source], logPath: null, cancellationToken: cancellationToken);

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

        long withoutMemory = await PeakMemoryAsync(processes, [java, "-cp", probes, "FootprintProbe"], cancellationToken);
        long withMemory = await PeakMemoryAsync(processes, [java, "-agentpath:" + agent, "-cp", probes, "FootprintProbe"], cancellationToken);
        long librarySize = new FileInfo(agent).Length;
        double withoutMedian = Median(withoutTimes);
        double withMedian = Median(withTimes);

        double startupWithout = Math.Round(withoutMedian, digits: 1);
        double startupWith = Math.Round(withMedian, digits: 1);
        double startupAdded = Math.Round(withMedian - withoutMedian, digits: 1);
        JsonObject result = [];
        result.SetProperty(propertyName: "rid", rid);
        result.SetProperty(propertyName: "java", major);
        result.SetProperty(propertyName: "librarySizeBytes", librarySize);
        result.SetProperty(propertyName: "startupWithoutAgentMs", startupWithout);
        result.SetProperty(propertyName: "startupWithAgentMs", startupWith);
        result.SetProperty(propertyName: "startupAddedMs", startupAdded);
        result.SetProperty(propertyName: "peakMemoryWithoutAgentKb", withoutMemory);
        result.SetProperty(propertyName: "peakMemoryWithAgentKb", withMemory);
        result.SetProperty(propertyName: "startupRuns", StartupRuns);

        JsonFile.WriteNode(
            Path.Combine(paths: [repository.Artifacts, "footprint", rid, $"hotspot-{major.ToString(CultureInfo.InvariantCulture)}.json"]),
            result
        );
        long? baselineSize = BaselineSize(repository, rid, major);
        Summarize(rid, major, librarySize, [startupWithout, startupWith, startupAdded, withoutMemory, withMemory], baselineSize);

        if (baselineSize is { } baseline)
        {
            Assert.True(
                librarySize <= baseline * SizeBudget,
                $"The agent library is {librarySize.ToString(CultureInfo.InvariantCulture)} bytes; the baseline is {baseline.ToString(CultureInfo.InvariantCulture)}."
            );
        }
    }

    private static long? BaselineSize(RepositoryContext repository, string rid, int major)
    {
        string path = repository.PathFromRoot(parts: ["docs", "footprint.baseline.json"]);

        if (!File.Exists(path))
            return null;

        JsonObject? byTarget = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        JsonObject? byMajor = Child(byTarget, rid);
        JsonObject? entry = Child(byMajor, major.ToString(CultureInfo.InvariantCulture));

        return entry is not null && entry.TryGetPropertyValue(propertyName: "librarySizeBytes", out JsonNode? size) ? size?.GetValue<long>() : null;
    }

    private static JsonObject? Child(JsonObject? parent, string name)
    {
        return parent is not null && parent.TryGetPropertyValue(name, out JsonNode? child) ? child?.AsObject() : null;
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
            ProcessResult result = await processes.RunAsync(command, logPath: null, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            Match match = PeakMemoryLine().Match(result.Output);

            if (match.Success)
                samples.Add(long.Parse(match.Groups[groupnum: 1].Value, CultureInfo.InvariantCulture));
        }

        return samples.Count == 0 ? -1 : (long)Median([.. samples.Select(static sample => (double)sample)]);
    }

    [GeneratedRegex(@"PEAK_RSS_KB=(-?\d+)")]
    private static partial Regex PeakMemoryLine();

    private static void Summarize(string rid, int major, long librarySize, double[] measured, long? baselineSize)
    {
        string baseline = baselineSize is { } size ? size.ToString(CultureInfo.InvariantCulture) + " bytes" : "none";
        string[] cells = [rid, "Java " + major.ToString(CultureInfo.InvariantCulture), librarySize.ToString(CultureInfo.InvariantCulture), .. measured.Select(static value => value.ToString(CultureInfo.InvariantCulture)), baseline];
        string line = "| " + string.Join(separator: " | ", cells) + " |";
        Console.WriteLine(line);

        if (Environment.GetEnvironmentVariable(variable: "GITHUB_STEP_SUMMARY") is not { Length: > 0 } summary)
            return;

        if (!File.Exists(summary) || new FileInfo(summary).Length == 0)
        {
            File.AppendAllText(
                summary,
                "| Target | Java | Library bytes | Start-up without (ms) | Start-up with (ms) | Added (ms) | Peak memory without (KB) | Peak memory with (KB) | Size baseline |"
                    + Environment.NewLine
                    + "|---|---|---|---|---|---|---|---|---|"
                    + Environment.NewLine
            );
        }

        File.AppendAllText(summary, line + Environment.NewLine);
    }

    private static async Task<double> TimeAsync(ProcessRunner processes, string[] command, CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        await processes.RunCheckedAsync(command, logPath: null, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}

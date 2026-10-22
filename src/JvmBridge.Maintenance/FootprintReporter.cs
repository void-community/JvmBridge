using System.Globalization;
using System.Text.Json.Nodes;

using JvmBridge.Build.Infrastructure;

namespace JvmBridge.Maintenance;

/// <summary>Turns the measurements that CI uploads into the size baseline the footprint job checks and the table published in docs/footprint.md.</summary>
internal sealed class FootprintReporter(RepositoryContext repository)
{
    internal void Update(string directory)
    {
        SortedDictionary<string, SortedDictionary<int, JsonObject>> measurements = Read(directory);

        if (measurements.Count == 0)
            throw new InvalidOperationException("No hotspot-*.json footprint measurements were found in " + directory + ".");

        JsonObject baseline = [];

        List<string> lines =
        [
            "# Footprint",
            string.Empty,
            "What loading an agent costs, measured on CI runners with a minimal agent (`samples/FootprintAgent`: one helper class and one patch) and `java -version` on HotSpot. Launches with and without the agent alternate, and the start-up figures are medians of 20 runs. Peak memory is the resident set high-water mark of a trivial Java program on Linux and is blank elsewhere.",
            string.Empty,
            "The library size of each row is the baseline that CI holds the next build to: growth beyond 10 percent fails the footprint job. Start-up time and memory are reported in each run's job summary but never fail a build, because shared runners are too noisy. Regenerate this page and the baseline deliberately with `dotnet run --project src/JvmBridge.Maintenance -- footprint <directory with the footprint-* artifacts>`.",
            string.Empty,
            "| Target | Java | Library (MiB) | Start-up without (ms) | Start-up with (ms) | Added (ms) | Peak memory added (MiB) |",
            "|---|---|---|---|---|---|---|"
        ];

        foreach ((string rid, SortedDictionary<int, JsonObject> byMajor) in measurements)
        {
            JsonObject majors = [];

            foreach ((int major, JsonObject entry) in byMajor)
            {
                long size = (long)Number(entry, name: "librarySizeBytes");
                JsonObject recorded = [];
                recorded.SetProperty(propertyName: "librarySizeBytes", size);
                majors.SetProperty(major.ToString(CultureInfo.InvariantCulture), recorded);
                lines.Add(Row(rid, major, entry, size));
            }

            baseline.SetProperty(rid, majors);
        }

        JsonFile.WriteNode(repository.PathFromRoot(parts: ["docs", "footprint.baseline.json"]), baseline);
        File.WriteAllLines(repository.PathFromRoot(parts: ["docs", "footprint.md"]), lines);
    }

    private static double Number(JsonObject entry, string name)
    {
        return entry.TryGetPropertyValue(name, out JsonNode? node) && node is not null ? node.GetValue<double>() : 0;
    }

    private static SortedDictionary<string, SortedDictionary<int, JsonObject>> Read(string directory)
    {
        SortedDictionary<string, SortedDictionary<int, JsonObject>> result = [];

        foreach (string file in Directory.EnumerateFiles(directory, searchPattern: "hotspot-*.json", SearchOption.AllDirectories))
        {
            JsonObject entry = JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidOperationException(file + " is empty.");
            string rid = entry.TryGetPropertyValue(propertyName: "rid", out JsonNode? ridNode) && ridNode is not null ? ridNode.GetValue<string>() : throw new InvalidOperationException(file + " has no rid.");
            int major = (int)Number(entry, name: "java");

            if (!result.TryGetValue(rid, out SortedDictionary<int, JsonObject>? byMajor))
            {
                byMajor = [];
                result[rid] = byMajor;
            }

            byMajor[major] = entry;
        }

        return result;
    }

    private static string Row(string rid, int major, JsonObject entry, long size)
    {
        double memoryAdded = Number(entry, name: "peakMemoryWithAgentKb") - Number(entry, name: "peakMemoryWithoutAgentKb");
        bool hasMemory = Number(entry, name: "peakMemoryWithoutAgentKb") > 0 && Number(entry, name: "peakMemoryWithAgentKb") > 0;
        string memory = hasMemory ? (memoryAdded / 1024).ToString(format: "0.0", CultureInfo.InvariantCulture) : string.Empty;

        string[] cells =
        [
            rid,
            "Java " + major.ToString(CultureInfo.InvariantCulture),
            (size / 1048576.0).ToString(format: "0.00", CultureInfo.InvariantCulture),
            Number(entry, name: "startupWithoutAgentMs").ToString(format: "0.0", CultureInfo.InvariantCulture),
            Number(entry, name: "startupWithAgentMs").ToString(format: "0.0", CultureInfo.InvariantCulture),
            Number(entry, name: "startupAddedMs").ToString(format: "0.0", CultureInfo.InvariantCulture),
            memory
        ];

        return "| " + string.Join(separator: " | ", cells) + " |";
    }
}

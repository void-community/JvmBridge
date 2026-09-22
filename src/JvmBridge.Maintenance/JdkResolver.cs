using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Maintenance;

internal sealed class JdkResolver(RepositoryContext repository, HttpService http)
{
    private readonly RepositoryContext _repository = repository;
    private readonly HttpService _http = http;

    public async Task ResolveAsync(CancellationToken cancellationToken)
    {
        CompatibilityConfiguration configuration = JsonFile.Read<CompatibilityConfiguration>(_repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "compatibility.json"]));

        List<JdkResolutionRequest> cells = [.. (
            from target in configuration.Targets
            where target.Agent
            from major in configuration.JavaMajors
            from implementation in configuration.Implementations
            select new JdkResolutionRequest(target, major, implementation))];

        List<JdkEntry> entries = [];

        foreach (IEnumerable<JdkResolutionRequest> batch in cells.Chunk(size: 8))
        {
            Task<JdkEntry>[] tasks = [.. batch.Select(cell => ResolveCellAsync(cell.Target, cell.Major, cell.Implementation, cancellationToken))];
            JdkEntry[] resolved = await Task.WhenAll(tasks).ConfigureAwait(continueOnCapturedContext: false);
            entries.AddRange(resolved);
        }

        string path = _repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "jdks.lock.json"]);

        if (File.Exists(path))
        {
            List<JdkEntry> previous = JsonFile.Read<JdkLock>(path).Jdks;

            HashSet<JvmMatrixCell> available = [.. entries
                .Where(entry => entry.Status == "available")
                .Select(entry => new JvmMatrixCell(entry.Rid, entry.Java, entry.Implementation))];

            List<JdkEntry> lost = [.. previous.Where(entry => entry.Status == "available" && !available.Contains(new JvmMatrixCell(entry.Rid, entry.Java, entry.Implementation)))];

            if (lost.Count > 0)
                throw new InvalidOperationException(message: "Previously available JVM coverage disappeared; review vendor availability before changing the lock.");
        }

        JsonFile.Write(path, new JdkLock { Jdks = entries });
        ConsoleOutput.WriteLine($"Pinned {entries.Count(entry => entry.Status == "available")} JVM archives; {entries.Count} matrix cells accounted for.");
    }

    public async Task<JdkArchive?> ResolveSemeruAsync(TargetConfiguration target, int major, CancellationToken cancellationToken)
    {
        string arch = Require(target.Arch, target.Rid + " architecture");

        if (target.Musl || arch is "arm" or "x86")
            return null;

        JsonNode? releaseNode = await _http.GetJsonAsync($"https://api.github.com/repos/ibmruntimes/semeru{major}-binaries/releases/latest", missing: true, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (releaseNode is null)
            return null;

        JsonObject release = releaseNode.RequireObject(description: "Semeru release");
        string os = Require(target.Os, target.Rid + " OS");
        string system = os == "mac" ? "mac" : os;
        string marker = $"jdk_{arch}_{system}_";
        string extension = system == "windows" ? ".zip" : ".tar.gz";
        JsonArray assets = release.Property(propertyName: "assets").RequireArray(description: "Semeru assets");

        foreach (JsonNode? node in assets)
        {
            JsonObject asset = node.RequireObject(description: "Semeru asset");
            string name = asset.Property(propertyName: "name").RequireString(description: "Semeru asset name");

            if (!name.Contains(marker, StringComparison.Ordinal) || !name.EndsWith(extension, StringComparison.Ordinal))
                continue;

            JsonObject? checksum = assets
                .Select((value, index) => value.RequireObject($"Semeru checksum asset {index}"))
                .FirstOrDefault(value => value.Property(propertyName: "name").RequireString(description: "Semeru checksum name") == name + ".sha256.txt") ?? throw new InvalidOperationException("Missing Semeru SHA256: " + name);

            string checksumUrl = checksum.Property(propertyName: "browser_download_url").RequireString(description: "Semeru checksum URL");
            string digest = (await _http.GetStringAsync(checksumUrl, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false) ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

            return new JdkArchive
            {
                Distribution = "semeru",
                Version = release.Property(propertyName: "tag_name").RequireString(description: "Semeru tag"),
                Url = asset.Property(propertyName: "browser_download_url").RequireString(description: "Semeru download URL"),
                Sha256 = digest
            };
        }

        return null;
    }

    public async Task<JdkArchive?> ResolveTemurinAsync(TargetConfiguration target, int major, CancellationToken cancellationToken)
    {
        string system = target.Musl ? "alpine-linux" : Require(target.Os, target.Rid + " OS");

        string query = Query([("architecture", Require(target.Arch, target.Rid + " architecture")), ("image_type", "jdk"), ("os", system)]);

        JsonArray entries = (await _http.GetJsonAsync($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?{query}", missing: true, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))?.RequireArray(description: "Temurin assets") ?? [];

        JsonObject? selected = entries
            .Select(static (entry, index) => entry.RequireObject($"Temurin asset {index}"))
            .OrderByDescending(
                static entry => entry.Property(propertyName: "version").Property(propertyName: "semver").RequireString(description: "Temurin version"),
                StringComparer.Ordinal
            )
            .FirstOrDefault();

        if (selected is null)
            return null;

        JsonObject package = selected.Property(propertyName: "binary").Property(propertyName: "package").RequireObject(description: "Temurin package");

        return new JdkArchive
        {
            Distribution = "temurin",
            Version = selected.Property(propertyName: "version").Property(propertyName: "semver").RequireString(description: "Temurin version"),
            Url = package.Property(propertyName: "link").RequireString(description: "Temurin link"),
            Sha256 = package.Property(propertyName: "checksum").RequireString(description: "Temurin checksum")
        };
    }

    public async Task<JdkArchive?> ResolveZuluAsync(TargetConfiguration target, int major, CancellationToken cancellationToken)
    {
        string os = Require(target.Os, target.Rid + " OS");
        string arch = Require(target.Arch, target.Rid + " architecture");

        string vendorArchitecture = arch switch
        {
            "x64" => "x86",
            "x86" => "x86",
            "arm" => "arm",
            "aarch64" => "arm",
            _ => throw new InvalidOperationException("Unsupported Zulu architecture: " + arch)
        };

        string query = Query(
            [
            ("java_version", major.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("os", os == "mac" ? "macos" : os),
            ("arch", vendorArchitecture),
            ("archive_type", os == "windows" ? "zip" : "tar.gz"),
            ("java_package_type", "jdk"),
            ("release_status", "ga"),
            ("availability_types", "CA"),
            ("latest", "true")
        ]
        );

        JsonArray entries = (await _http.GetJsonAsync("https://api.azul.com/metadata/v1/zulu/packages/?" + query, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).RequireArray(description: "Zulu packages");

        string[] architectureMarkers = arch switch
        {
            "x64" => ["_x64"],
            "x86" => ["_i686", "_i386"],
            "arm" => ["_aarch32hf"],
            "aarch64" => ["_aarch64"],
            _ => throw new InvalidOperationException("Unsupported Zulu architecture: " + arch)
        };

        List<JsonObject> candidates = [];

        foreach (JsonNode? node in entries)
        {
            JsonObject entry = node.RequireObject(description: "Zulu package");
            string name = entry.Property(propertyName: "name").RequireString(description: "Zulu package name");

            bool excludedVariant = name.Contains(value: "-fx-", StringComparison.Ordinal) || name.Contains(value: "-crac-", StringComparison.Ordinal);

            if (!architectureMarkers.Any(name.Contains) || excludedVariant)
                continue;

            if (name.Contains(value: "musl", StringComparison.Ordinal) != target.Musl)
                continue;

            candidates.Add(entry);
        }

        JsonObject? selected = candidates.MaxBy(VersionKey);

        if (selected is null)
            return null;

        string uuid = selected.Property(propertyName: "package_uuid").RequireString(description: "Zulu package UUID");
        JsonObject details = (await _http.GetJsonAsync("https://api.azul.com/metadata/v1/zulu/packages/" + uuid, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).RequireObject(description: "Zulu package details");

        return new JdkArchive
        {
            Distribution = "zulu",
            Version = string.Join(separator: '.', ReadVersion(selected.Property(propertyName: "java_version"), description: "Zulu Java version")),
            Url = selected.Property(propertyName: "download_url").RequireString(description: "Zulu download URL"),
            Sha256 = details.Property(propertyName: "sha256_hash").RequireString(description: "Zulu checksum")
        };
    }

    private static string Query(IEnumerable<(string Name, string Value)> values)
    {
        return string.Join(separator: '&', values.Select(static value => Uri.EscapeDataString(value.Name) + "=" + Uri.EscapeDataString(value.Value)));
    }

    private static List<int> ReadVersion(JsonNode? node, string description)
    {
        return [.. node.RequireArray(description).Select((value, index) => value.RequireInt32($"{description} part {index}"))];
    }

    private static string Require(string? value, string description)
    {
        return value ?? throw new InvalidOperationException($"Missing {description}.");
    }

    private static string VersionKey(JsonObject entry)
    {
        string java = string.Join(
            separator: '.',
            ReadVersion(entry.Property(propertyName: "java_version"), description: "Zulu Java version").Select(static value => value.ToString(format: "D8", System.Globalization.CultureInfo.InvariantCulture))
        );

        JsonNode? distro = entry.Property(propertyName: "distro_version");

        string distroValue = distro is JsonArray ? string.Join(
            separator: '.',
            ReadVersion(distro, description: "Zulu distribution version").Select(static value => value.ToString(format: "D8", System.Globalization.CultureInfo.InvariantCulture))
        ) : distro?.ToJsonString() ?? string.Empty;

        return java + "|" + distroValue;
    }

    private async Task<JdkEntry> ResolveCellAsync(TargetConfiguration target, int major, string implementation, CancellationToken cancellationToken)
    {
        JdkArchive? archive = implementation == "openj9"
            ? await ResolveSemeruAsync(target, major, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
            : await ResolveTemurinAsync(target, major, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) ?? await ResolveZuluAsync(target, major, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (archive is null)
        {
            return new JdkEntry
            {
                Rid = target.Rid,
                Java = major,
                Implementation = implementation,
                Status = "unavailable",
                Reason = "No matching GA JDK archive in the configured vendor inventories."
            };
        }

        if (!Regex.IsMatch(archive.Sha256, pattern: "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Invalid SHA256 for " + archive.Url);

        return new JdkEntry
        {
            Rid = target.Rid,
            Java = major,
            Implementation = implementation,
            Distribution = archive.Distribution,
            Version = archive.Version,
            Url = archive.Url,
            Sha256 = archive.Sha256,
            Status = "available"
        };
    }
}

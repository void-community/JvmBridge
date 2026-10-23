using System.Globalization;
using System.Text.Json;

using JvmBridge.Build.Models;

namespace JvmBridge.Build.Infrastructure;

/// <summary>
/// Finds and unpacks the checksum-pinned JDKs of the repository's inventory. Each JDK is cached once under the temporary directory, so tests and builds on a machine share one copy.
/// The assembly that compiles this file must embed <c>jdks.lock.json</c> under that name, and may embed <c>legacy-jdks.lock.json</c>, the Java 6 and 7 JDKs that only tests run on.
/// </summary>
internal static class PinnedJdk
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Finds the inventory entry for a JDK, preferring an available one.</summary>
    internal static JdkEntry? Find(int major, string implementation, string runtimeIdentifier)
    {
        List<JdkEntry> entries = Read(name: "jdks.lock.json") ?? throw new InvalidOperationException(message: "The pinned JDK inventory is missing.");
        entries.AddRange(Read(name: "legacy-jdks.lock.json") ?? []);
        JdkEntry[] matches = [.. entries.Where(entry => entry.Rid == runtimeIdentifier && entry.Java == major && entry.Implementation == implementation)];

        return matches.FirstOrDefault(entry => entry.Status == "available") ?? matches.FirstOrDefault();
    }

    /// <summary>Downloads, verifies and unpacks a pinned JDK unless a complete copy is already cached.</summary>
    /// <returns>The JDK home directory, which contains <c>bin/java</c>.</returns>
    internal static async Task<string> UnpackAsync(JdkEntry entry, CancellationToken cancellationToken)
    {
        string sha = entry.Sha256 ?? throw new InvalidOperationException(message: "Missing JDK SHA256.");
        string name = $"{entry.Implementation}-{entry.Java.ToString(CultureInfo.InvariantCulture)}-{sha[..12]}";
        string cache = Path.Combine(Path.GetTempPath(), path2: "jvmbridge-testing", path3: "jdks", name);
        string marker = Path.Combine(cache, path2: "home.txt");

        if (File.Exists(marker))
        {
            string relativeHome = (await File.ReadAllTextAsync(marker, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).Trim();
            string cached = Path.Combine(cache, relativeHome);

            if (Directory.Exists(cached))
                return cached;
        }

        string staging = cache + ".staging-" + Guid.NewGuid().ToString(format: "N");

        using HttpService http = new();

        string home = await new JdkArchiveManager(http).UnpackAsync(ToArchive(entry), staging, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        string relative = Path.GetRelativePath(staging, home);
        await File.WriteAllTextAsync(Path.Combine(staging, path2: "home.txt"), relative, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        try
        {
            Directory.Move(staging, cache);
        }
        catch (IOException) when (Directory.Exists(cache))
        {
            // Another process unpacked the same JDK first; use its copy.
            Directory.Delete(staging, recursive: true);
        }

        return Path.Combine(cache, relative);
    }

    private static List<JdkEntry>? Read(string name)
    {
        using Stream? stream = typeof(PinnedJdk).Assembly.GetManifestResourceStream(name);

        if (stream is null)
            return null;

        JdkLock inventory = JsonSerializer.Deserialize<JdkLock>(stream, JsonOptions) ?? throw new InvalidOperationException("The pinned JDK inventory " + name + " is empty.");

        return inventory.Jdks;
    }

    private static JdkArchive ToArchive(JdkEntry entry)
    {
        return new JdkArchive
        {
            Distribution = entry.Distribution ?? throw new InvalidOperationException(message: "Missing JDK distribution."),
            Version = entry.Version ?? throw new InvalidOperationException(message: "Missing JDK version."),
            Url = entry.Url ?? throw new InvalidOperationException(message: "Missing JDK URL."),
            Sha256 = entry.Sha256 ?? throw new InvalidOperationException(message: "Missing JDK SHA256.")
        };
    }
}

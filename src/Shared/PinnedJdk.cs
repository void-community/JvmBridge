using System.Globalization;

using JvmBridge.Build.Models;

namespace JvmBridge.Build.Infrastructure;

/// <summary>
/// Finds and unpacks the checksum-pinned JDKs of <see cref="PinnedJdks"/>, which the assembly that compiles this file compiles too. Each JDK is cached once under the temporary
/// directory, so tests and builds on a machine share one copy.
/// </summary>
internal static class PinnedJdk
{
    /// <summary>Finds the inventory entry for a JDK, preferring an available one. The Java 6 and 7 JDKs that only tests run on are searched only when <paramref name="includeLegacy"/> is set.</summary>
    internal static JdkEntry? Find(int major, string implementation, string runtimeIdentifier, bool includeLegacy = false)
    {
        IEnumerable<JdkEntry> inventory = includeLegacy ? PinnedJdks.Jdks.Concat(PinnedJdks.LegacyJdks) : PinnedJdks.Jdks;
        JdkEntry[] matches = [.. inventory.Where(entry => entry.Rid == runtimeIdentifier && entry.Java == major && entry.Implementation == implementation)];

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

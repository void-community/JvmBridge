using JvmBridge.Build.Models;

namespace JvmBridge.Build;

internal static class ManifestValidator
{
    internal static void ValidateManifest(CompatibilityConfiguration configuration, IReadOnlyCollection<JdkEntry> entries)
    {
        HashSet<(string Rid, int Java, string Implementation)> expected = [.. (
            from target in configuration.Targets
            where target.Agent
            from major in configuration.JavaMajors
            from implementation in configuration.Implementations
            select (target.Rid, major, implementation))];

        List<(string Rid, int Java, string Implementation)> actual = [.. entries.Select(entry => (entry.Rid, entry.Java, entry.Implementation))];

        if (actual.Distinct().Count() != actual.Count || !actual.ToHashSet().SetEquals(expected))
            throw new InvalidOperationException(message: "Missing or duplicate manifest cells.");

        foreach (JdkEntry entry in entries)
        {
            if (entry.Status is not ("available" or "unavailable"))
                throw new InvalidOperationException("Unknown JDK availability status: " + entry.Status);

            bool validDigest = entry.Sha256 is { Length: 64 } && entry.Sha256.All(Uri.IsHexDigit);
            bool validUrl = Uri.TryCreate(entry.Url, UriKind.Absolute, out Uri? address) && address.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(address.Host);
            bool unpinned = !validDigest || !validUrl;

            if (entry.Status == "available" && unpinned)
                throw new InvalidOperationException(message: "Unpinned JDK archive");

            if (entry.Status == "unavailable" && string.IsNullOrEmpty(entry.Reason))
                throw new InvalidOperationException(message: "Missing unavailability reason");
        }
    }
}

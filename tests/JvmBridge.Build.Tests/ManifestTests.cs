using Xunit;

using JvmBridge.Build.Models;

namespace JvmBridge.Build.Tests;

/// <summary>
/// Verifies that every configured JVM matrix cell is explicit and reproducible.
/// </summary>
public sealed class ManifestTests
{
    /// <summary>
    /// Verifies that a complete cell with an HTTPS archive and checksum is accepted.
    /// </summary>
    [Fact]
    public void CompletePinnedManifestIsAccepted()
    {
        ManifestValidator.ValidateManifest(Configuration(), [AvailableEntry()]);
    }

    /// <summary>
    /// Verifies that duplicate matrix cells are rejected.
    /// </summary>
    [Fact]
    public void DuplicateManifestCellsAreRejected()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(static () => ManifestValidator.ValidateManifest(Configuration(), [AvailableEntry(), AvailableEntry()]));

        Assert.Contains(expectedSubstring: "Missing or duplicate", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that unavailable cells explain the limitation.
    /// </summary>
    [Fact]
    public void UnavailableArchiveRequiresReason()
    {
        JdkEntry entry = new()
        {
            Rid = "linux-x64",
            Java = 17,
            Implementation = "hotspot",
            Status = "unavailable"
        };

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => ManifestValidator.ValidateManifest(Configuration(), [entry]));

        Assert.Contains(expectedSubstring: "reason", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Unknown statuses cannot silently remove required cells from execution.</summary>
    [Fact]
    public void UnknownStatusIsRejected()
    {
        JdkEntry entry = AvailableEntry();
        entry.Status = "timeout";
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => ManifestValidator.ValidateManifest(Configuration(), [entry]));
        Assert.Contains(expectedSubstring: "Unknown", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that available archives must use HTTPS and carry a checksum.
    /// </summary>
    [Fact]
    public void UnpinnedAvailableArchiveIsRejected()
    {
        JdkEntry entry = new()
        {
            Rid = "linux-x64",
            Java = 17,
            Implementation = "hotspot",
            Distribution = "fixture",
            Version = "17.0.1",
            Url = "http://example.invalid/jdk.tar.gz",
            Status = "available"
        };

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => ManifestValidator.ValidateManifest(Configuration(), [entry]));

        Assert.Contains(expectedSubstring: "Unpinned", failure.Message, StringComparison.Ordinal);
    }

    private static JdkEntry AvailableEntry()
    {
        return new JdkEntry
        {
            Rid = "linux-x64",
            Java = 17,
            Implementation = "hotspot",
            Distribution = "fixture",
            Version = "17.0.1",
            Url = "https://example.invalid/jdk.tar.gz",
            Sha256 = new string(c: 'a', count: 64),
            Status = "available"
        };
    }

    private static CompatibilityConfiguration Configuration()
    {
        return new CompatibilityConfiguration
        {
            JavaMajors = [17],
            Implementations = ["hotspot"],
            Targets = [new TargetConfiguration { Rid = "linux-x64", Agent = true }]
        };
    }
}

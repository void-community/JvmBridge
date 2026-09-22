using System.Text.Json.Nodes;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

using Xunit;

namespace JvmBridge.Build.Tests;

/// <summary>Verifies that native evidence matches the pinned inventory, not merely a Java major.</summary>
public sealed class CoverageTests
{
    /// <summary>Only a passing result for the exact pinned archive establishes coverage.</summary>
    [Fact]
    public void ExactPinnedResultIsAccepted()
    {
        JdkEntry entry = Entry();
        JsonObject result = JsonFile.ToObject(entry);
        result.SetProperty(propertyName: "status", value: "passed");
        CoverageValidator.Validate([entry], [result]);
    }

    /// <summary>Missing cells, failed cells, and different archive digests cannot satisfy coverage.</summary>
    /// <param name="status">Reported test outcome.</param>
    /// <param name="checksum">Reported archive digest.</param>
    [Theory]
    [InlineData("passed", "different")]
    [InlineData("failed", "pinned")]
    [InlineData("unavailable", "pinned")]
    public void InvalidEvidenceIsRejected(string status, string checksum)
    {
        JdkEntry entry = Entry();
        JsonObject result = JsonFile.ToObject(entry);
        result.SetProperty(propertyName: "status", status);
        result.SetProperty(propertyName: "sha256", checksum);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => CoverageValidator.Validate([entry], [result]));
        Assert.Contains(expectedSubstring: "JVM cell", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A missing result must fail aggregation.</summary>
    [Fact]
    public void MissingResultIsRejected()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(static () => CoverageValidator.Validate([Entry()], []));
        Assert.Contains(expectedSubstring: "Unverified", failure.Message, StringComparison.Ordinal);
    }

    private static JdkEntry Entry()
    {
        return new()
        {
            Rid = "linux-arm64",
            Java = 25,
            Implementation = "hotspot",
            Distribution = "fixture",
            Version = "25.0.1",
            Sha256 = "pinned",
            Status = "available"
        };
    }
}

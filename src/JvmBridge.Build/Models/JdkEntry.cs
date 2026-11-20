namespace JvmBridge.Build.Models;

internal sealed class JdkEntry
{
    // The cell: the JVM of one Java version and implementation on one runtime identifier.
    public required string Implementation { get; init; }
    public int Java { get; init; }
    public required string Rid { get; init; }

    // The archive that provides the JDK, pinned by its checksum.
    public string? Distribution { get; init; }
    public string? Sha256 { get; init; }
    public string? Url { get; init; }
    public string? Version { get; init; }

    // Whether the cell is available, and the reason when it is not.
    public string? Reason { get; init; }
    public required string Status { get; set; }
}

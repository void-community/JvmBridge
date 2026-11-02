namespace JvmBridge.Build.Models;

internal sealed class JdkEntry
{
    public string? Distribution { get; init; }

    public required string Implementation { get; init; }

    public int Java { get; init; }

    public string? Reason { get; init; }

    public required string Rid { get; init; }

    public string? Sha256 { get; init; }

    public required string Status { get; set; }

    public string? Url { get; init; }

    public string? Version { get; init; }
}

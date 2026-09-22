using System.Text.Json.Serialization;

namespace JvmBridge.Build.Models;

internal sealed class JdkEntry
{
    [JsonPropertyOrder(3)]
    public string? Distribution { get; init; }

    [JsonPropertyOrder(2)]
    public required string Implementation { get; init; }

    [JsonPropertyOrder(1)]
    public int Java { get; init; }

    [JsonPropertyOrder(8)]
    public string? Reason { get; init; }

    [JsonPropertyOrder(0)]
    public required string Rid { get; init; }

    [JsonPropertyOrder(6)]
    public string? Sha256 { get; init; }

    [JsonPropertyOrder(7)]
    public required string Status { get; set; }

    [JsonPropertyOrder(5)]
    public string? Url { get; init; }

    [JsonPropertyOrder(4)]
    public string? Version { get; init; }
}

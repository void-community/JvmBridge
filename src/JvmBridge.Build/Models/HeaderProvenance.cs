using System.Text.Json.Serialization;

namespace JvmBridge.Build.Models;

internal sealed class HeaderProvenance
{
    [JsonPropertyOrder(2)]
    public required string Archive { get; init; }

    [JsonPropertyOrder(0)]
    public required string Distribution { get; init; }

    [JsonPropertyOrder(4)]
    public required Dictionary<string, string> Headers { get; init; }

    [JsonPropertyOrder(3)]
    public required string Sha256 { get; init; }

    [JsonPropertyOrder(1)]
    public required string Version { get; init; }
}

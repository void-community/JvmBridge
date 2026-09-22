namespace JvmBridge.Build.Models;

internal sealed class BuildSummary
{
    public required string Rid { get; init; }

    public string Status { get; init; } = "build-verified";
}

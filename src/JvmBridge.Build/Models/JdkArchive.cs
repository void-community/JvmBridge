namespace JvmBridge.Build.Models;

internal sealed class JdkArchive
{
    public required string Distribution { get; init; }

    public required string Sha256 { get; init; }

    public required string Url { get; init; }

    public required string Version { get; init; }
}

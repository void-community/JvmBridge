namespace JvmBridge.Build.Models;

internal sealed class TargetConfiguration
{
    public bool Agent { get; init; }

    public string? Arch { get; init; }

    public bool Build { get; init; }

    public string? Container { get; init; }

    public string? CrossContainer { get; init; }

    public bool Emulated { get; init; }

    public bool Musl { get; init; }

    public string? Os { get; init; }

    public string? Reason { get; init; }

    public required string Rid { get; init; }

    public string? Runner { get; init; }

    public string? Status { get; init; }
}

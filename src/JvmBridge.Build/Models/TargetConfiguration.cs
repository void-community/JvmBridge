namespace JvmBridge.Build.Models;

internal sealed class TargetConfiguration
{
    // The platform that the runtime identifier names.
    public string? Arch { get; init; }
    public bool Musl { get; init; }
    public string? Os { get; init; }
    public required string Rid { get; init; }

    // What is built for the target, and the container that a cross build runs in.
    public bool Agent { get; init; }
    public bool Build { get; init; }
    public string? CrossContainer { get; init; }

    // Whether the target is supported, and the reason when it is not.
    public string? Reason { get; init; }
    public string? Status { get; init; }
}

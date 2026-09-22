namespace JvmBridge.Build.Models;

internal sealed class VmOptionRule
{
    public required string Architecture { get; init; }

    public required string Implementation { get; init; }

    public int Java { get; init; }

    public required List<string> Options { get; init; }

    public required string Reason { get; init; }
}

namespace JvmBridge.Build.Models;

internal sealed class VmOptionRule
{
    // The JVMs that the rule applies to.
    public required string Architecture { get; init; }
    public required string Implementation { get; init; }
    public int Java { get; init; }
    public string? Os { get; init; }

    // The options those JVMs are started with, and why they need them.
    public required List<string> Options { get; init; }
    public required string Reason { get; init; }
}

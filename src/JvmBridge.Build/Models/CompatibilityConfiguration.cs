namespace JvmBridge.Build.Models;

internal sealed class CompatibilityConfiguration
{
    public required List<string> Implementations { get; init; }

    public required List<int> JavaMajors { get; init; }

    public required List<TargetConfiguration> Targets { get; init; }

    public List<VmOptionRule> VmOptionRules { get; init; } = [];
}

namespace JvmBridge.Agents;

/// <summary>What happened to one loaded class when a patch was registered or removed at run time.</summary>
/// <param name="ClassName">The internal name of the class.</param>
/// <param name="Failure">Why the class could not be retransformed, or <see langword="null"/> when it was.</param>
public sealed record PatchClassReport(string ClassName, string? Failure)
{
    /// <summary>Gets whether the class was retransformed.</summary>
    public bool Succeeded => Failure is null;
}

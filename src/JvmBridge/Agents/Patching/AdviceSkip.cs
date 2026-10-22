namespace JvmBridge.Agents;

/// <summary>Why advice could not be applied to a method.</summary>
/// <param name="Category">The kind of reason.</param>
/// <param name="Reason">The reason in words.</param>
internal readonly record struct AdviceSkip(PatchSkipCategory Category, string Reason);

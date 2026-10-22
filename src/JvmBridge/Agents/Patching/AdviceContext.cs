namespace JvmBridge.Agents;

/// <summary>What the engine knows about the advice of one class while it applies the patches to it: whether the class is being retransformed, which methods were advised when it was defined, and which are advised now.</summary>
internal sealed class AdviceContext(bool isRetransformation, IReadOnlySet<string>? recorded)
{
    /// <summary>Gets whether the class is being retransformed: it is loaded already, so it cannot gain methods.</summary>
    internal bool IsRetransformation { get; } = isRetransformation;

    /// <summary>Gets the methods (<c>name+descriptor</c>) that were advised when the class was defined, or <see langword="null"/> when it was defined without advice.</summary>
    internal IReadOnlySet<string>? Recorded { get; } = recorded;

    /// <summary>Gets the methods that the patches advised in this pass.</summary>
    internal HashSet<string> Applied { get; } = [];
}

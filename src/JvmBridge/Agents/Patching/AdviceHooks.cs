using JvmBridge.ClassFiles;

namespace JvmBridge.Agents;

/// <summary>The hooks that advice puts around one method.</summary>
/// <param name="Before">The static method called before the method runs, or <see langword="null"/> for none.</param>
/// <param name="After">The static method called after the method returns, or <see langword="null"/> for none.</param>
/// <param name="IncludeArguments">Whether the after hook receives the arguments of the call.</param>
internal sealed record AdviceHooks(JavaMethod? Before, JavaMethod? After, bool IncludeArguments)
{
    /// <summary>Describes which hooks there are, for a log line.</summary>
    /// <returns>For example <c>Before and After</c>.</returns>
    internal string Describe()
    {
        if (Before is not null && After is not null)
            return "Before and After";

        return Before is not null ? "Before" : After is not null ? "After" : "forwarding";
    }
}

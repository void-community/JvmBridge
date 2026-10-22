namespace JvmBridge.Tasks;

/// <summary>What the proxy generator needs to know about a class file: its access flags and its methods.</summary>
/// <param name="AccessFlags">The class access flags.</param>
/// <param name="Methods">The methods, including constructors.</param>
internal sealed record ClassSummary(int AccessFlags, IReadOnlyList<ClassMethodSummary> Methods);

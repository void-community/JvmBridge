namespace JvmBridge.Tasks;

/// <summary>A method of a class file: its access flags, name and descriptor.</summary>
/// <param name="AccessFlags">The method access flags.</param>
/// <param name="Name">The method name.</param>
/// <param name="Descriptor">The method descriptor.</param>
internal sealed record ClassMethodSummary(int AccessFlags, string Name, string Descriptor);

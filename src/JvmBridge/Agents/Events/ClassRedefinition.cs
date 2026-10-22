namespace JvmBridge.Agents;

/// <summary>A class and the bytes that replace it in <see cref="AgentContext.RedefineClasses"/>.</summary>
/// <param name="Type">A class reference that is valid in the current callback.</param>
/// <param name="Bytes">The new class-file bytes.</param>
public readonly record struct ClassRedefinition(nint Type, ReadOnlyMemory<byte> Bytes);

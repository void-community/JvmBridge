namespace JvmBridge.Agents;

/// <summary>Class bytes borrowed only for the duration of TransformClass. Return owned bytes to replace them.</summary>
public readonly ref struct ClassFile(string? name, bool isRetransformation, ReadOnlySpan<byte> bytes)
{
    /// <summary>Gets the binary class name, or <see langword="null"/> when the JVM supplies none.</summary>
    public string? Name { get; } = name;

    /// <summary>Gets whether the bytes represent a retransformation of an already loaded class.</summary>
    public bool IsRetransformation { get; } = isRetransformation;

    /// <summary>Gets class bytes borrowed only for the current callback.</summary>
    public ReadOnlySpan<byte> Bytes { get; } = bytes;
}

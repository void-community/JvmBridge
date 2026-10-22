namespace JvmBridge.Tool.Commands;

/// <summary>A class file read from a path, a directory or a jar.</summary>
/// <param name="Name">Where the class came from, for messages.</param>
/// <param name="Bytes">The class-file bytes.</param>
internal sealed record ClassInput(string Name, byte[] Bytes);

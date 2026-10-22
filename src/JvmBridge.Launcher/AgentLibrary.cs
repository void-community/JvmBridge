namespace JvmBridge.Launcher;

/// <summary>A native agent library and the runtime identifier of the platform it runs on.</summary>
/// <param name="Rid">The runtime identifier, for example <c>linux-x64</c>.</param>
/// <param name="FileName">The file name of the library, for example <c>MyAgent.so</c>.</param>
/// <param name="Bytes">The contents of the library.</param>
internal sealed record AgentLibrary(string Rid, string FileName, byte[] Bytes)
{
    /// <summary>Reads a published library from disk.</summary>
    /// <param name="rid">The runtime identifier of the platform it runs on.</param>
    /// <param name="path">The path of the library.</param>
    /// <returns>The library.</returns>
    internal static AgentLibrary FromFile(string rid, string path)
    {
        return new(rid, Path.GetFileName(path), File.ReadAllBytes(path));
    }
}

namespace JvmBridge.Launcher;

/// <summary>A published native agent library and the runtime identifier of the platform it runs on.</summary>
/// <param name="Rid">The runtime identifier, for example <c>linux-x64</c>.</param>
/// <param name="Path">The path of the native library.</param>
internal sealed record AgentLibrary(string Rid, string Path);

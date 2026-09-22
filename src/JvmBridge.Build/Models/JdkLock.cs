namespace JvmBridge.Build.Models;

internal sealed class JdkLock
{
    public required List<JdkEntry> Jdks { get; init; }
}

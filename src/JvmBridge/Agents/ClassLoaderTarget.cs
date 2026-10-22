namespace JvmBridge.Agents;

/// <summary>Selects the class loader that defines a helper class.</summary>
public enum ClassLoaderTarget
{
    /// <summary>Makes the helper visible to every loader by appending a temporary jar to the bootstrap search path when the VM initializes, or when the agent attaches late. This works on every JVM, including OpenJ9 under checked JNI, which rejects a null loader.</summary>
    Bootstrap,

    /// <summary>Defines the helper once in each loader that defines a patched class, before the first patched class from that loader, for helpers that must see application classes.</summary>
    PatchedClassLoader,
}

namespace JvmBridge.Agents;

/// <summary>Selects the class loader that defines a helper class.</summary>
public enum ClassLoaderTarget
{
    /// <summary>
    /// Makes the helper visible to every loader by defining it in the bootstrap loader when the VM initializes, or when the agent attaches late. On OpenJ9, whose checked JNI rejects a null loader, a temporary jar is appended to
    /// the bootstrap search path instead; doing that on HotSpot would disable class-data sharing for application classes.
    /// </summary>
    Bootstrap,

    /// <summary>Defines the helper once in each loader that defines a patched class, before the first patched class from that loader, for helpers that must see application classes.</summary>
    PatchedClassLoader,
}

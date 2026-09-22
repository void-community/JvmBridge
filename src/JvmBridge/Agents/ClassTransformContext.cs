using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>JNI context and references borrowed only for the current class-file callback. Callbacks may be reentrant.</summary>
public readonly ref struct ClassTransformContext
{
    /// <summary>Gets the callback's thread-owned JNI environment, or <see langword="null"/> when unavailable.</summary>
    public JavaEnvironment? Environment { get; }

    /// <summary>Gets the defining loader reference, or zero for a bootstrap class.</summary>
    public nint Loader { get; }

    /// <summary>Gets the protection-domain reference, or zero when the JVM supplies none.</summary>
    public nint ProtectionDomain { get; }

    /// <summary>Gets the existing class reference, or zero on initial definition.</summary>
    public nint ClassBeingRedefined { get; }

    /// <summary>Gets whether ordinary JNI calls are permitted by the callback's JVMTI phase and an environment is available.</summary>
    public bool CanUseJni { get; }

    internal ClassTransformContext(JavaEnvironment? environment, nint loader, nint protectionDomain, nint classBeingRedefined, bool canUseJni)
    {
        Environment = environment;
        Loader = loader;
        ProtectionDomain = protectionDomain;
        ClassBeingRedefined = classBeingRedefined;
        CanUseJni = canUseJni;
    }
}

using JvmBridge.Native;
using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>Agent state and checked JVMTI operations. RawHandle is an explicit unsafe escape hatch.</summary>
public sealed unsafe class AgentContext
{
    private readonly jvmtiInterface_1_** _environment;
    private bool _disposed;
    internal JavaAgent Agent { get; }
    internal bool EventsEnabled { get; set; }

    /// <summary>Gets the JVM that owns this tooling environment.</summary>
    public JavaVirtualMachine VirtualMachine { get; }

    /// <summary>Gets the modified UTF-8 agent options decoded as UTF-16.</summary>
    public string Options { get; }

    /// <summary>Gets whether the agent was loaded into an already running JVM.</summary>
    public bool IsAttached { get; }

    /// <summary>Gets the JVMTI version reported by the tooling environment.</summary>
    public int Version { get; }

    /// <summary>Gets whether class retransformation was enabled during configuration.</summary>
    public bool CanRetransform { get; private set; }

    /// <summary>Gets the raw JVMTI environment pointer while this context remains active.</summary>
    public nint RawHandle { get { EnsureAccessible(); return (nint)_environment; } }

    internal AgentContext(JavaAgent agent, nint machine, string options, bool attached)
    {
        Agent = agent;
        Options = options;
        IsAttached = attached;
        VirtualMachine = JavaVirtualMachine.Borrow(machine);
        _environment = (jvmtiInterface_1_**)VirtualMachine.GetToolingEnvironment();
        int version = 0;
        Check((*_environment)->GetVersionNumber(_environment, &version), nameof(Version));
        Version = version;
    }

    /// <summary>Attempts to enable the JVMTI class-retransformation capability during configuration.</summary>
    /// <returns><see langword="true"/> when retransformation is available and was enabled; otherwise, <see langword="false"/>.</returns>
    public bool TryEnableRetransformation()
    {
        EnsureAccessible();

        if (EventsEnabled)
            throw new InvalidOperationException("Request capabilities from Configure, before events are enabled.");
        jvmtiCapabilities available = default;
        Check((*_environment)->GetPotentialCapabilities(_environment, &available), nameof(TryEnableRetransformation));
        if (available.can_retransform_classes == 0)
            return false;
        jvmtiCapabilities requested = default;
        requested.can_retransform_classes = 1;
        Check((*_environment)->AddCapabilities(_environment, &requested), nameof(TryEnableRetransformation));
        CanRetransform = true;
        return true;
    }

    /// <summary>Requests exactly the supplied capabilities; invoke during Configure before events are enabled.</summary>
    /// <param name="requested">The capability flags to request from the JVM.</param>
    public void RequestCapabilities(jvmtiCapabilities requested)
    {
        EnsureAccessible();

        if (EventsEnabled)
            throw new InvalidOperationException("Request capabilities from Configure, before events are enabled.");
        Check((*_environment)->AddCapabilities(_environment, &requested), nameof(RequestCapabilities));
        CanRetransform |= requested.can_retransform_classes != 0;
    }

    /// <summary>Gets the JNI type signature for a class reference supplied by the current callback.</summary>
    /// <param name="type">The callback-local JNI class reference.</param>
    /// <returns>The modified UTF-8 class signature decoded as UTF-16.</returns>
    public string GetClassSignature(nint type)
    {
        EnsureAccessible();

        byte* signature = null;
        Check((*_environment)->GetClassSignature(_environment, (_jobject*)type, &signature, null), nameof(GetClassSignature));
        try { return ModifiedUtf8.Decode(signature); }
        finally { Check((*_environment)->Deallocate(_environment, signature), nameof(GetClassSignature)); }
    }

    /// <summary>Requests retransformation of a class after the capability has been enabled.</summary>
    /// <param name="type">The JNI class reference to retransform.</param>
    public void Retransform(nint type)
    {
        EnsureAccessible();

        if (!CanRetransform)
            throw new NotSupportedException("Retransformation was not enabled before class hooks were registered.");
        _jobject* value = (_jobject*)type;
        Check((*_environment)->RetransformClasses(_environment, 1, &value), nameof(Retransform));
    }

    /// <summary>Gets raw class references for every class currently loaded by the JVM.</summary>
    /// <param name="environment">The JNI environment for the current thread and callback scope.</param>
    /// <returns>The loaded class references owned by the current JNI local-reference scope.</returns>
    public nint[] GetLoadedClasses(JavaEnvironment environment)
    {
        EnsureAccessible();
        environment.EnsureAccessible();
        int count = 0;
        _jobject** classes = null;
        Check((*_environment)->GetLoadedClasses(_environment, &count, &classes), nameof(GetLoadedClasses));
        try
        {
            nint[] values = new nint[count];
            for (int index = 0; index < count; index++)
                values[index] = (nint)classes[index];
            return values;
        }
        finally { Check((*_environment)->Deallocate(_environment, (byte*)classes), nameof(GetLoadedClasses)); }
    }

    internal static void Check(jvmtiError result, string operation)
    {
        if (result != jvmtiError.JVMTI_ERROR_NONE)
            throw new InvalidOperationException($"{operation} failed with JVMTI error {result}.");
    }

    private void EnsureAccessible() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal void Dispose()
    {
        if (_disposed)
            return;
        Check((*_environment)->DisposeEnvironment(_environment), nameof(Dispose));
        _disposed = true;
    }
}

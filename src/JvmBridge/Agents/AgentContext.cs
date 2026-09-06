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
    public JavaVirtualMachine VirtualMachine { get; }
    public string Options { get; }
    public bool IsAttached { get; }
    public int Version { get; }
    public bool CanRetransform { get; private set; }
    public nint RawHandle { get { ObjectDisposedException.ThrowIf(_disposed, this); return (nint)_environment; } }

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

    public bool TryEnableRetransformation()
    {
        _ = RawHandle;
        if (EventsEnabled)
            throw new InvalidOperationException("Request capabilities during agent initialization, before events are enabled.");
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

    /// <summary>Requests exactly the supplied capabilities; invoke during OnLoad/OnAttach before events are enabled.</summary>
    public void RequestCapabilities(jvmtiCapabilities requested)
    {
        _ = RawHandle;
        if (EventsEnabled)
            throw new InvalidOperationException("Request capabilities during agent initialization, before events are enabled.");
        Check((*_environment)->AddCapabilities(_environment, &requested), nameof(RequestCapabilities));
        CanRetransform |= requested.can_retransform_classes != 0;
    }

    public string GetClassSignature(nint type)
    {
        _ = RawHandle;
        byte* signature = null;
        Check((*_environment)->GetClassSignature(_environment, (_jobject*)type, &signature, null), nameof(GetClassSignature));
        try { return ModifiedUtf8.Decode(signature); }
        finally { Check((*_environment)->Deallocate(_environment, signature), nameof(GetClassSignature)); }
    }

    public void Retransform(nint type)
    {
        _ = RawHandle;
        if (!CanRetransform)
            throw new NotSupportedException("Retransformation was not enabled before class hooks were registered.");
        _jobject* value = (_jobject*)type;
        Check((*_environment)->RetransformClasses(_environment, 1, &value), nameof(Retransform));
    }

    public nint[] GetLoadedClasses(JavaEnvironment environment)
    {
        _ = RawHandle;
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

    internal void Dispose()
    {
        if (_disposed)
            return;
        Check((*_environment)->DisposeEnvironment(_environment), nameof(Dispose));
        _disposed = true;
    }
}

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
        jvmtiError result = (*_environment)->GetVersionNumber(_environment, &version);
        if (result != jvmtiError.JVMTI_ERROR_NONE)
        {
            Check((*_environment)->DisposeEnvironment(_environment), "Dispose failed initialization");
            Check(result, nameof(Version));
        }
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
        string result;
        try
        {
            Check((*_environment)->GetClassSignature(_environment, (_jobject*)type, &signature, null), nameof(GetClassSignature));
            result = ModifiedUtf8.Decode(signature);
        }
        catch
        {
            FreeUnchecked((nint)signature);
            throw;
        }
        Free((nint)signature);
        return result;
    }

    /// <summary>Gets the defining class loader as an owned thread-local reference, or an empty reference for a bootstrap class. Call only in a live JVM phase.</summary>
    public JavaLocalReference GetClassLoader(JavaEnvironment environment, nint type)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentOutOfRangeException.ThrowIfZero(type);
        EnsureAccessible();
        environment.EnsureAccessible();
        _jobject* loader = null;
        jvmtiError result = (*_environment)->GetClassLoader(_environment, (_jobject*)type, &loader);
        if (result != jvmtiError.JVMTI_ERROR_NONE)
        {
            if (loader != null)
                environment.DeleteLocal((nint)loader);
            Check(result, nameof(GetClassLoader));
        }
        return new JavaLocalReference(environment, (nint)loader);
    }

    /// <summary>Gets the Java class access flags in a live JVM phase.</summary>
    public int GetClassModifiers(nint type)
    {
        ArgumentOutOfRangeException.ThrowIfZero(type);
        EnsureAccessible();
        int modifiers = 0;
        Check((*_environment)->GetClassModifiers(_environment, (_jobject*)type, &modifiers), nameof(GetClassModifiers));
        return modifiers;
    }

    /// <summary>Checks whether a class can be modified in a live JVM phase.</summary>
    public bool IsModifiableClass(nint type)
    {
        ArgumentOutOfRangeException.ThrowIfZero(type);
        EnsureAccessible();
        byte modifiable = 0;
        Check((*_environment)->IsModifiableClass(_environment, (_jobject*)type, &modifiable), nameof(IsModifiableClass));
        return modifiable != 0;
    }

    /// <summary>Copies declared field metadata from a prepared class in the live phase. IDs do not retain their declaring class or loader.</summary>
    public JavaFieldInfo[] GetDeclaredFields(nint type)
    {
        ArgumentOutOfRangeException.ThrowIfZero(type);
        EnsureAccessible();
        int count = 0;
        _jfieldID** fields = null;
        JavaFieldInfo[] result;
        try
        {
            Check((*_environment)->GetClassFields(_environment, (_jobject*)type, &count, &fields), nameof(GetDeclaredFields));
            result = new JavaFieldInfo[count];
            for (int index = 0; index < count; index++)
                result[index] = CopyField(type, fields[index]);
        }
        catch
        {
            FreeUnchecked((nint)fields);
            throw;
        }
        Free((nint)fields);
        return result;
    }

    /// <summary>Copies declared method metadata from a prepared class in the live phase. IDs do not retain their declaring class or loader.</summary>
    public JavaMethodInfo[] GetDeclaredMethods(nint type)
    {
        ArgumentOutOfRangeException.ThrowIfZero(type);
        EnsureAccessible();
        int count = 0;
        _jmethodID** methods = null;
        JavaMethodInfo[] result;
        try
        {
            Check((*_environment)->GetClassMethods(_environment, (_jobject*)type, &count, &methods), nameof(GetDeclaredMethods));
            result = new JavaMethodInfo[count];
            for (int index = 0; index < count; index++)
                result[index] = CopyMethod(methods[index]);
        }
        catch
        {
            FreeUnchecked((nint)methods);
            throw;
        }
        Free((nint)methods);
        return result;
    }

    private JavaFieldInfo CopyField(nint type, _jfieldID* field)
    {
        byte* name = null;
        byte* descriptor = null;
        byte* generic = null;
        JavaFieldInfo result;
        try
        {
            Check((*_environment)->GetFieldName(_environment, (_jobject*)type, field, &name, &descriptor, &generic), nameof(GetDeclaredFields));
            int modifiers = 0;
            Check((*_environment)->GetFieldModifiers(_environment, (_jobject*)type, field, &modifiers), nameof(GetDeclaredFields));
            result = new JavaFieldInfo((nint)field, ModifiedUtf8.Decode(name), ModifiedUtf8.Decode(descriptor), generic == null ? null : ModifiedUtf8.Decode(generic), modifiers);
        }
        catch
        {
            FreeUnchecked((nint)name, (nint)descriptor, (nint)generic);
            throw;
        }
        Free((nint)name, (nint)descriptor, (nint)generic);
        return result;
    }

    private JavaMethodInfo CopyMethod(_jmethodID* method)
    {
        byte* name = null;
        byte* descriptor = null;
        byte* generic = null;
        JavaMethodInfo result;
        try
        {
            Check((*_environment)->GetMethodName(_environment, method, &name, &descriptor, &generic), nameof(GetDeclaredMethods));
            int modifiers = 0;
            Check((*_environment)->GetMethodModifiers(_environment, method, &modifiers), nameof(GetDeclaredMethods));
            result = new JavaMethodInfo((nint)method, ModifiedUtf8.Decode(name), ModifiedUtf8.Decode(descriptor), generic == null ? null : ModifiedUtf8.Decode(generic), modifiers);
        }
        catch
        {
            FreeUnchecked((nint)name, (nint)descriptor, (nint)generic);
            throw;
        }
        Free((nint)name, (nint)descriptor, (nint)generic);
        return result;
    }

    private void Free(params nint[] buffers)
    {
        jvmtiError firstFailure = jvmtiError.JVMTI_ERROR_NONE;
        foreach (nint buffer in buffers)
        {
            if (buffer == 0)
                continue;
            jvmtiError status = (*_environment)->Deallocate(_environment, (byte*)buffer);
            if (firstFailure == jvmtiError.JVMTI_ERROR_NONE)
                firstFailure = status;
        }
        Check(firstFailure, nameof(Free));
    }

    private void FreeUnchecked(params nint[] buffers)
    {
        foreach (nint buffer in buffers)
            if (buffer != 0)
                _ = (*_environment)->Deallocate(_environment, (byte*)buffer);
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
        nint[] values;
        try
        {
            Check((*_environment)->GetLoadedClasses(_environment, &count, &classes), nameof(GetLoadedClasses));
            values = new nint[count];
            for (int index = 0; index < count; index++)
                values[index] = (nint)classes[index];
        }
        catch
        {
            if (classes != null)
                for (int index = 0; index < count; index++)
                    environment.DeleteLocal((nint)classes[index]);
            FreeUnchecked((nint)classes);
            throw;
        }
        Free((nint)classes);
        return values;
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

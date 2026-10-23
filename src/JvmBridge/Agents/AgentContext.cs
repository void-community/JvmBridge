using System.Buffers;

using JvmBridge.Native;
using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>Agent state and checked JVMTI operations. RawHandle is an explicit unsafe escape hatch.</summary>
public sealed unsafe class AgentContext
{
    private readonly jvmtiInterface_1_** _environment;
    private readonly PatchEngine _patches;
    private int? _javaMajor;
    private bool _disposed;
    internal JavaAgent Agent { get; }
    internal bool EventsEnabled { get; set; }

    /// <summary>Gets the name that prefixes this agent's log lines: the simple name of the agent's assembly, as set by the generated entry points.</summary>
    public string Name { get; }

    /// <summary>Gets the events that this agent receives. Subscribe from <see cref="JavaAgent.Configure"/> or later; JvmBridge owns the callback table, so never call <c>SetEventCallbacks</c> through <see cref="RawHandle"/>.</summary>
    public AgentEvents Events { get; }

    /// <summary>Gets the JVM that owns this tooling environment.</summary>
    public JavaVirtualMachine VirtualMachine { get; }

    /// <summary>Gets the modified UTF-8 agent options decoded as UTF-16.</summary>
    public string Options { get; }

    /// <summary>Gets <see cref="Options"/> parsed as comma-separated <c>key=value</c> entries and flags.</summary>
    public AgentOptions ParsedOptions { get; }

    /// <summary>Gets or sets the lowest severity that <see cref="Log(AgentLogLevel, string)"/> writes. It starts as the <c>log</c> option (<c>log=debug</c>, <c>log=none</c>, ...) and is <see cref="AgentLogLevel.Information"/> without one.</summary>
    public AgentLogLevel LogLevel { get; set; }

    /// <summary>Gets whether the agent was loaded into an already running JVM.</summary>
    public bool IsAttached { get; }

    /// <summary>Gets the JVMTI version reported by the tooling environment.</summary>
    public int Version { get; }

    /// <summary>Gets whether class retransformation was enabled during configuration.</summary>
    public bool CanRetransform { get; private set; }

    /// <summary>Gets the raw JVMTI environment pointer while this context remains active.</summary>
    public nint RawHandle { get { EnsureAccessible(); return (nint)_environment; } }

    internal AgentContext(JavaAgent agent, nint machine, string options, bool attached, string? name = null)
    {
        Agent = agent;
        Name = name ?? DefaultName(agent);
        _patches = new PatchEngine(this);
        Events = new AgentEvents(this);
        Options = options;
        ParsedOptions = AgentOptions.Parse(options);
        LogLevel = ParsedOptions.TryGetValue("log", out string? level) ? AgentLog.ParseLevel(level, AgentLogLevel.Information) : AgentLogLevel.Information;
        IsAttached = attached;
        VirtualMachine = JavaVirtualMachine.Borrow(machine);
        _environment = (jvmtiInterface_1_**)OpenToolingEnvironment(VirtualMachine);
        int version = 0;
        jvmtiError result = (*_environment)->GetVersionNumber(_environment, &version);
        if (result != jvmtiError.JVMTI_ERROR_NONE)
        {
            Check((*_environment)->DisposeEnvironment(_environment), "Dispose failed initialization");
            Check(result, nameof(Version));
        }
        Version = version;
    }

    internal PatchEngine Patches => _patches;

    /// <summary>Gets the patches that are registered now, in the order the runtime applies them. A tool reads it to preview what an agent would change; <see cref="Patch"/> and <see cref="Unpatch"/> change it.</summary>
    public IReadOnlyList<ClassPatch> RegisteredPatches => _patches.Registered;

    /// <summary>
    /// Registers a declarative class patch. The runtime applies it, in registration order, to the classes that load from now on and logs what it did. When the agent attaches late, and when a patch is registered after
    /// <see cref="JavaAgent.Configure"/>, it also retransforms the matching classes that are already loaded, if retransformation is enabled; <see cref="PatchNow"/> reports what happened to each of those.
    /// </summary>
    /// <param name="patch">The patch to register. Registering the same instance again does nothing.</param>
    public void Patch(ClassPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        EnsureAccessible();

        if (!EventsEnabled)
        {
            if (IsAttached && !CanRetransform)
                _ = TryEnableRetransformation();

            _patches.Add(patch);

            return;
        }

        _ = _patches.Register(patch);
    }

    /// <summary>Registers a patch and retransforms the loaded classes it targets, then reports what happened to each. Needs the retransformation capability, which an attached agent has and a loaded agent requests with <see cref="TryEnableRetransformation"/> in <see cref="JavaAgent.Configure"/>.</summary>
    /// <param name="patch">The patch to register.</param>
    /// <returns>The classes that were retransformed and those that could not be, or why none was looked at.</returns>
    public PatchReport PatchNow(ClassPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        EnsureAccessible();

        if (EventsEnabled)
            return _patches.Register(patch);

        _patches.Add(patch);

        return new PatchReport("events are not enabled yet; the patch is registered and applies as classes load", []);
    }

    /// <summary>Removes a registered patch and retransforms the loaded classes it targeted, so they are transformed again without it. Classes that load afterwards are no longer patched by it.</summary>
    /// <param name="patch">The patch to remove, the same instance that was registered.</param>
    /// <returns>The classes that were retransformed and those that could not be, or why none was looked at.</returns>
    public PatchReport Unpatch(ClassPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        EnsureAccessible();

        return _patches.Withdraw(patch);
    }

    /// <summary>Declares a helper class to define in the JVM. Call from <see cref="JavaAgent.Configure"/>.</summary>
    /// <param name="definition">The helper class and anything it needs.</param>
    /// <param name="target">Whether to define it in the bootstrap loader or in each loader that defines a patched class.</param>
    public void DefineClass(JavaClassDefinition definition, ClassLoaderTarget target)
    {
        ArgumentNullException.ThrowIfNull(definition);
        EnsureAccessible();

        if (EventsEnabled)
            throw new InvalidOperationException("Declare helper classes from Configure, before events are enabled.");

        if (!Enum.IsDefined(target))
            throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown class loader target.");

        _patches.Add(definition, target);
    }

    /// <summary>Logs an <see cref="AgentLogLevel.Information"/> line. By default the line is written prefixed with <see cref="Name"/> to standard error; <see cref="AgentLog.Sink"/> redirects it. It is safe in every callback, needs no ICU, and never throws.</summary>
    /// <param name="message">The text to write.</param>
    public void Log(string message)
    {
        Log(AgentLogLevel.Information, message);
    }

    /// <summary>Logs a line unless <paramref name="level"/> is below <see cref="LogLevel"/>. It is safe in every callback and never throws.</summary>
    /// <param name="level">The severity of the line.</param>
    /// <param name="message">The text to write.</param>
    public void Log(AgentLogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (IsLogEnabled(level))
            AgentLog.Write(level, Name, message);
    }

    /// <summary>Gets whether a line of this severity would be written, so callers can skip building an expensive message.</summary>
    /// <param name="level">The severity to test.</param>
    /// <returns><see langword="true"/> when <paramref name="level"/> is at or above <see cref="LogLevel"/> and not <see cref="AgentLogLevel.None"/>.</returns>
    public bool IsLogEnabled(AgentLogLevel level)
    {
        return level != AgentLogLevel.None && level >= LogLevel;
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
        _granted |= 1 << (int)JavaCapability.RetransformClasses;
        return true;
    }

    /// <summary>Gets whether the JVM is OpenJ9 (or IBM J9), whose checked JNI rejects defining a class with a null loader.</summary>
    internal bool IsOpenJ9 => GetSystemProperty("java.vm.name") is { } name && name.Contains("J9", StringComparison.Ordinal);

    /// <summary>
    /// Gets the Java major version of the JVM, from its <c>java.specification.version</c>: 6 for <c>1.6</c>, 17 for <c>17</c>, or 0 when the JVM does not say. Use it to leave out what an older JVM does not have; every function JvmBridge itself
    /// calls exists from Java 6.
    /// </summary>
    public int JavaMajor => _javaMajor ??= ParseJavaMajor(GetSystemProperty("java.specification.version"));

    /// <summary>Reads a Java major version from a specification version such as <c>1.8</c> or <c>21</c>.</summary>
    /// <param name="specification">The specification version, or <see langword="null"/>.</param>
    /// <returns>The major version, or 0 when the text is not one.</returns>
    internal static int ParseJavaMajor(string? specification)
    {
        if (specification is null)
            return 0;

        string text = specification.StartsWith("1.", StringComparison.Ordinal) ? specification[2..] : specification;
        int dot = text.IndexOf('.', StringComparison.Ordinal);

        return int.TryParse(dot < 0 ? text : text[..dot], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int major) ? major : 0;
    }

    // JVMTI 1.2 came with Java 7 and late Java 6 builds; early Java 6 builds offer 1.1 and 1.0. Every function JvmBridge calls is in 1.1 or 1.0.
    private static nint OpenToolingEnvironment(JavaVirtualMachine machine)
    {
        foreach (uint version in new[] { Methods.JVMTI_VERSION_1_2, Methods.JVMTI_VERSION_1_1, Methods.JVMTI_VERSION_1_0 })
        {
            try { return machine.GetToolingEnvironment((int)version); }
            catch (JniException failure) when (failure.ErrorCode == Methods.JNI_EVERSION) { continue; }
        }

        throw new JniException(Methods.JNI_EVERSION, operation: "GetEnv for JVMTI 1.2, 1.1 or 1.0");
    }

    internal string? GetSystemProperty(string property)
    {
        EnsureAccessible();
        byte* value = null;

        fixed (byte* encoded = ModifiedUtf8.Encode(property))
        {
            jvmtiError result = (*_environment)->GetSystemProperty(_environment, encoded, &value);

            if (result != jvmtiError.JVMTI_ERROR_NONE)
            {
                FreeUnchecked((nint)value);

                return null;
            }
        }

        string text = ModifiedUtf8.Decode(value);
        Free((nint)value);

        return text;
    }

    internal void AddToBootstrapSearch(string path)
    {
        EnsureAccessible();

        fixed (byte* segment = ModifiedUtf8.Encode(path))
            Check((*_environment)->AddToBootstrapClassLoaderSearch(_environment, segment), nameof(AddToBootstrapSearch));
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

    private int _granted;

    /// <summary>Gets whether a capability has been granted to this agent.</summary>
    /// <param name="capability">The JVMTI capability to look up among those the JVM gave this agent.</param>
    /// <returns><see langword="true"/> when the JVM granted it.</returns>
    public bool IsGranted(JavaCapability capability)
    {
        return (_granted & (1 << (int)capability)) != 0 || (capability == JavaCapability.RetransformClasses && CanRetransform);
    }

    /// <summary>
    /// Asks the JVM for a capability, which it grants only if it offers it in its current phase. A JVM that started the agent through <c>-agentpath</c> offers more than one that the agent was attached to.
    /// Retransformation must be requested before events are enabled, so request it from <see cref="JavaAgent.Configure"/>; the other capabilities can be requested later.
    /// </summary>
    /// <param name="capability">The JVMTI capability to ask the JVM for; the JVM decides whether it can grant it.</param>
    /// <returns><see langword="true"/> when the capability was granted, now or earlier.</returns>
    public bool TryRequest(JavaCapability capability)
    {
        EnsureAccessible();

        if (IsGranted(capability))
            return true;

        if (capability == JavaCapability.RetransformClasses)
            return TryEnableRetransformation();

        jvmtiCapabilities available = default;
        Check((*_environment)->GetPotentialCapabilities(_environment, &available), nameof(TryRequest));

        if (!IsPotential(available, capability))
            return false;

        jvmtiCapabilities requested = default;
        Select(ref requested, capability);
        Check((*_environment)->AddCapabilities(_environment, &requested), nameof(TryRequest));
        _granted |= 1 << (int)capability;

        return true;
    }

    internal void SetEventEnabled(jvmtiEvent notification, bool enabled)
    {
        EnsureAccessible();
        jvmtiInterface_1_** environment = _environment;
        jvmtiEventMode mode = enabled ? jvmtiEventMode.JVMTI_ENABLE : jvmtiEventMode.JVMTI_DISABLE;
        Check(
            ((delegate* unmanaged[Cdecl]<jvmtiInterface_1_**, jvmtiEventMode, jvmtiEvent, _jobject*, jvmtiError>)(*environment)->SetEventNotificationMode)(environment, mode, notification, null),
            operation: "SetEventNotificationMode"
        );
    }

    private static bool IsPotential(jvmtiCapabilities available, JavaCapability capability)
    {
        return capability == JavaCapability.RedefineClasses
            ? available.can_redefine_classes != 0
            : capability == JavaCapability.GenerateExceptionEvents
            ? available.can_generate_exception_events != 0
            : capability == JavaCapability.GenerateMethodEntryEvents
            ? available.can_generate_method_entry_events != 0
            : capability == JavaCapability.GenerateMethodExitEvents
            ? available.can_generate_method_exit_events != 0
            : capability == JavaCapability.GenerateMonitorEvents
            ? available.can_generate_monitor_events != 0
            : capability == JavaCapability.GenerateGarbageCollectionEvents
            ? available.can_generate_garbage_collection_events != 0
            : available.can_retransform_classes != 0;
    }

    private static void Select(ref jvmtiCapabilities requested, JavaCapability capability)
    {
        if (capability == JavaCapability.RedefineClasses)
            requested.can_redefine_classes = 1;
        else if (capability == JavaCapability.GenerateExceptionEvents)
            requested.can_generate_exception_events = 1;
        else if (capability == JavaCapability.GenerateMethodEntryEvents)
            requested.can_generate_method_entry_events = 1;
        else if (capability == JavaCapability.GenerateMethodExitEvents)
            requested.can_generate_method_exit_events = 1;
        else if (capability == JavaCapability.GenerateMonitorEvents)
            requested.can_generate_monitor_events = 1;
        else if (capability == JavaCapability.GenerateGarbageCollectionEvents)
            requested.can_generate_garbage_collection_events = 1;
        else
            requested.can_retransform_classes = 1;
    }

    /// <summary>Resolves a raw method ID, as events and stack frames give it, to its class, name and descriptor.</summary>
    /// <param name="environment">The JNI environment of the current thread and callback scope.</param>
    /// <param name="method">The raw method ID.</param>
    /// <returns>The method's declaring class, name and descriptor.</returns>
    public JavaMethodName DescribeMethod(JavaEnvironment environment, nint method)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentOutOfRangeException.ThrowIfZero(method);
        EnsureAccessible();
        environment.EnsureAccessible();
        byte* name = null;
        byte* descriptor = null;
        byte* generic = null;
        Check((*_environment)->GetMethodName(_environment, (_jmethodID*)method, &name, &descriptor, &generic), nameof(DescribeMethod));
        string methodName = ModifiedUtf8.Decode(name);
        string methodDescriptor = ModifiedUtf8.Decode(descriptor);
        Free((nint)name, (nint)descriptor, (nint)generic);
        _jobject* declaring = null;
        Check((*_environment)->GetMethodDeclaringClass(_environment, (_jmethodID*)method, &declaring), nameof(DescribeMethod));

        try
        {
            string signature = GetClassSignature((nint)declaring);

            return new JavaMethodName(signature.Length > 2 && signature[index: 0] == 'L' ? signature[1..^1] : signature, methodName, methodDescriptor);
        }
        finally
        {
            environment.DeleteLocal((nint)declaring);
        }
    }

    /// <summary>Gets the live Java threads as local references, which belong to the current JNI local-reference scope and are released when it ends, for example when the native method that called this returns.</summary>
    /// <param name="environment">The JNI environment of the current thread and callback scope.</param>
    /// <returns>The thread references.</returns>
    public nint[] GetAllThreads(JavaEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        EnsureAccessible();
        environment.EnsureAccessible();
        int count = 0;
        _jobject** threads = null;
        Check((*_environment)->GetAllThreads(_environment, &count, &threads), nameof(GetAllThreads));
        nint[] values = new nint[count];

        for (int index = 0; index < count; index++)
            values[index] = (nint)threads[index];

        Free((nint)threads);

        return values;
    }

    /// <summary>Gets the top frames of a thread's stack, innermost first.</summary>
    /// <param name="environment">The JNI environment of the current thread and callback scope.</param>
    /// <param name="thread">A thread reference, or 0 for the current thread.</param>
    /// <param name="maxFrames">The most frames to return.</param>
    /// <returns>The frames.</returns>
    public JavaStackFrame[] GetStackTrace(JavaEnvironment environment, nint thread, int maxFrames = 64)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrames, other: 1);
        EnsureAccessible();
        environment.EnsureAccessible();
        jvmtiFrameInfo[] buffer = new jvmtiFrameInfo[maxFrames];
        int count = 0;

        fixed (jvmtiFrameInfo* frames = buffer)
            Check((*_environment)->GetStackTrace(_environment, (_jobject*)thread, 0, maxFrames, frames, &count), nameof(GetStackTrace));

        JavaStackFrame[] result = new JavaStackFrame[count];

        for (int index = 0; index < count; index++)
            result[index] = new JavaStackFrame(DescribeMethod(environment, (nint)buffer[index].method), buffer[index].location);

        return result;
    }

    /// <summary>Gets the name, priority and daemon flag of a thread.</summary>
    /// <param name="environment">The JNI environment of the current thread and callback scope.</param>
    /// <param name="thread">A thread reference, or 0 for the current thread.</param>
    /// <returns>What the JVM knows about the thread.</returns>
    public JavaThreadInfo GetThreadInfo(JavaEnvironment environment, nint thread)
    {
        ArgumentNullException.ThrowIfNull(environment);
        EnsureAccessible();
        environment.EnsureAccessible();
        jvmtiThreadInfo info = default;
        Check((*_environment)->GetThreadInfo(_environment, (_jobject*)thread, &info), nameof(GetThreadInfo));

        // The group and the context loader are local references that this call created; the name is JVMTI memory.
        if (info.thread_group != null)
            environment.DeleteLocal((nint)info.thread_group);

        if (info.context_class_loader != null)
            environment.DeleteLocal((nint)info.context_class_loader);

        string name = info.name == null ? string.Empty : ModifiedUtf8.Decode(info.name);
        Free((nint)info.name);

        return new JavaThreadInfo(name, info.priority, info.is_daemon != 0);
    }

    /// <summary>Gets the state bits of a thread.</summary>
    /// <param name="thread">A thread reference.</param>
    /// <returns>The state flags.</returns>
    public JavaThreadState GetThreadState(nint thread)
    {
        EnsureAccessible();
        int state = 0;
        Check((*_environment)->GetThreadState(_environment, (_jobject*)thread, &state), nameof(GetThreadState));

        return (JavaThreadState)state;
    }

    /// <summary>Replaces the bytes of loaded classes. The new bytes may only change method bodies, never the shape of a class. Needs <see cref="JavaCapability.RedefineClasses"/>.</summary>
    /// <param name="definitions">The classes and their new bytes.</param>
    public void RedefineClasses(IReadOnlyList<ClassRedefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        EnsureAccessible();

        if (!IsGranted(JavaCapability.RedefineClasses))
            throw new NotSupportedException(message: "Redefinition needs the RedefineClasses capability; request it with TryRequest.");

        if (definitions.Count == 0)
            return;

        jvmtiClassDefinition[] native = new jvmtiClassDefinition[definitions.Count];
        MemoryHandle[] pins = new MemoryHandle[definitions.Count];

        try
        {
            for (int index = 0; index < definitions.Count; index++)
            {
                pins[index] = definitions[index].Bytes.Pin();
                native[index].klass = (_jobject*)definitions[index].Type;
                native[index].class_byte_count = definitions[index].Bytes.Length;
                native[index].class_bytes = (byte*)pins[index].Pointer;
            }

            fixed (jvmtiClassDefinition* values = native)
                Check((*_environment)->RedefineClasses(_environment, native.Length, values), nameof(RedefineClasses));
        }
        finally
        {
            foreach (MemoryHandle pin in pins)
                pin.Dispose();
        }
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

    /// <summary>Requests retransformation of several classes at once, returning the JVMTI error name instead of throwing so that a caller can retry a smaller batch.</summary>
    internal string? TryRetransform(nint[] types)
    {
        EnsureAccessible();

        if (!CanRetransform)
            return "retransformation was not enabled before class hooks were registered";

        fixed (nint* values = types)
        {
            jvmtiError result = (*_environment)->RetransformClasses(_environment, types.Length, (_jobject**)values);

            return result == jvmtiError.JVMTI_ERROR_NONE ? null : result.ToString();
        }
    }

    /// <summary>Gets the JVMTI phase.</summary>
    internal jvmtiPhase GetPhase()
    {
        EnsureAccessible();
        jvmtiPhase phase = default;
        Check((*_environment)->GetPhase(_environment, &phase), nameof(GetPhase));

        return phase;
    }

    /// <summary>Gets the interfaces that a class directly implements or extends as local references, which the caller must delete. A class that is not prepared yields none.</summary>
    internal nint[] GetImplementedInterfaces(JavaEnvironment environment, nint type)
    {
        EnsureAccessible();
        int count = 0;
        _jobject** interfaces = null;
        jvmtiError result = (*_environment)->GetImplementedInterfaces(_environment, (_jobject*)type, &count, &interfaces);

        if (result != jvmtiError.JVMTI_ERROR_NONE)
        {
            FreeUnchecked((nint)interfaces);

            return [];
        }

        nint[] values = new nint[count];

        for (int index = 0; index < count; index++)
            values[index] = (nint)interfaces[index];

        Free((nint)interfaces);

        return values;
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

    // Assembly metadata is deliberately not read: AssemblyName and Assembly.FullName create a CultureInfo, and without libicu that aborts the process.
    private static string DefaultName(JavaAgent agent)
    {
        Type type = agent.GetType();
        string? root = type.Namespace?.Split('.')[0];

        return string.IsNullOrEmpty(root) ? type.Name : root;
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

using System.Runtime.InteropServices;
using System.Text;

using JvmBridge.Agents;
using JvmBridge.Native;
using JvmBridge.Runtime;

namespace HelloAgent;

/// <summary>Demonstrates native registration, transformations, and thread-owned JNI references.</summary>
internal sealed unsafe class BridgeAgent : JavaAgent
{
    private static BridgeAgent? s_agent;

    private readonly List<(JavaGlobalReference Loader, JavaGlobalReference Class)> _helpers = [];

    private JavaWeakGlobalReference? _observedWeak;
    private JavaGlobalReference? _targetLoader;

    /// <summary>Gets the context of the running agent, for the native callbacks that are kept in other files.</summary>
    internal static AgentContext? Context { get; private set; }

    /// <inheritdoc/>
    public override void Configure(AgentContext context)
    {
        if (!context.TryEnableRetransformation())
            Log(message: "RETRANSFORM_UNAVAILABLE");

        PatchDemo.Register(context);
        ModuleDemo.Register(context);

        Interop.Context = context;

        context.DefineClass(Interop.Definition, ClassLoaderTarget.Bootstrap);

        // Redefinition is requested up front; the events that need capabilities are denied without an exception on a JVM that does not offer them.
        if (!context.TryRequest(JavaCapability.RedefineClasses))
            Log(message: "REDEFINE_UNAVAILABLE");

        // HotSpot offers the method entry and exit capabilities only to an agent that is loaded at startup, so they are requested here and the events are subscribed later, around the one call that is traced.
        bool tracing = context.TryRequest(JavaCapability.GenerateMethodEntryEvents)
            && context.TryRequest(JavaCapability.GenerateMethodExitEvents);

        if (!tracing)
            Log(message: "METHOD_EVENTS_UNAVAILABLE");

        JavaEvents events = JavaEvents.ThreadStart | JavaEvents.ThreadEnd | JavaEvents.ClassLoad | JavaEvents.Exception | JavaEvents.GarbageCollectionStart | JavaEvents.GarbageCollectionFinish | JavaEvents.MonitorContendedEnter | JavaEvents.MonitorContendedEntered;

        Interop.Subscription = context.Events.Subscribe(events).ToString();
    }

    /// <inheritdoc/>
    public override void OnAttach(AgentContext context)
    {
        OnLoad(context);
        Log(message: "ATTACH");

        using JavaThreadAttachment attachment = context.VirtualMachine.AttachCurrentThread();

        JavaEnvironment environment = attachment.Environment;

        InstallSystemHelper(environment);
        LoadedMarker.Mark(environment);

        int localCapacity = 4096;
        int capacityResult = environment.Functions->EnsureLocalCapacity((JNINativeInterface_**)environment.Handle, localCapacity);

        environment.ThrowIfException(operation: "EnsureLocalCapacity");

        if (capacityResult != 0)
            throw new JniException(capacityResult, operation: "EnsureLocalCapacity");

        nint[] classes = context.GetLoadedClasses(environment);

        try
        {
            foreach (nint type in classes)
            {
                if (context.GetClassSignature(type) == "LBridgeFixture;")
                {
                    Register(environment, type);

                    if (context.CanRetransform)
                        context.Retransform(type);
                }
            }
        }
        finally
        {
            foreach (nint type in classes)
                environment.Functions->DeleteLocalRef((JNINativeInterface_**)environment.Handle, (_jobject*)type);
        }
    }

    /// <inheritdoc/>
    public override void OnClassLoad(AgentContext context, JavaEnvironment environment, nint thread, nint type) => Interop.ClassLoaded();

    /// <inheritdoc/>
    public override void OnClassPrepare(AgentContext context, JavaEnvironment environment, nint type)
    {
        if (context.GetClassSignature(type) == "LBridgeFixture;")
            Register(environment, type);
    }

    /// <inheritdoc/>
    public override void OnCommand(AgentContext context, string command)
    {
        IReadOnlyList<PatchReport> reports = command switch
        {
            "unpatch" => PatchDemo.Unpatch(context),
            "patch" => PatchDemo.PatchNow(context),
            _ => throw new InvalidOperationException("Unknown command " + command)
        };

        foreach (PatchReport report in reports)
        {
            Log(message: $"COMMAND {command}: {report}");

            if (report.NotRetransformedReason is not null || report.Failed > 0)
                throw new InvalidOperationException("The command " + command + " did not retransform every class: " + report);
        }
    }

    /// <inheritdoc/>
    public override void OnException(AgentContext context, JavaEnvironment environment, JavaExceptionEvent thrown) => Interop.ExceptionThrown();

    /// <inheritdoc/>
    public override void OnGarbageCollectionFinish(AgentContext context) => Interop.CollectionFinished();

    /// <inheritdoc/>
    public override void OnGarbageCollectionStart(AgentContext context) => Interop.CollectionStarted();

    /// <inheritdoc/>
    public override void OnLoad(AgentContext context)
    {
        Context = context;
        s_agent = this;

        if (context.Options.Contains(value: "fail-start", StringComparison.Ordinal))
            throw new InvalidOperationException(message: "Intentional initialization failure.");

        Log(message: $"LOAD retransform={context.CanRetransform}");
    }

    /// <inheritdoc/>
    public override void OnMethodEntry(AgentContext context, JavaEnvironment environment, nint thread, nint method) => Interop.MethodEntered(context,
        environment,
        method);

    /// <inheritdoc/>
    public override void OnMethodExit(AgentContext context, JavaEnvironment environment, nint thread, nint method, bool poppedByException) => Interop.MethodExited();

    /// <inheritdoc/>
    public override void OnMonitorContendedEnter(AgentContext context, JavaEnvironment environment, nint thread, nint monitor) => Interop.MonitorBlocking();

    /// <inheritdoc/>
    public override void OnMonitorContendedEntered(AgentContext context, JavaEnvironment environment, nint thread, nint monitor) => Interop.MonitorAcquired();

    /// <inheritdoc/>
    public override void OnThreadEnd(AgentContext context, JavaEnvironment environment, nint thread) => Interop.ThreadEnded(context,
        environment,
        thread);

    /// <inheritdoc/>
    public override void OnThreadStart(AgentContext context, JavaEnvironment environment, nint thread) => Interop.ThreadStarted(context,
        environment,
        thread);

    /// <inheritdoc/>
    public override void OnUnload(AgentContext context)
    {
        _observedWeak?.Dispose();

        _observedWeak = null;

        _targetLoader?.Dispose();

        _targetLoader = null;

        foreach ((JavaGlobalReference loader, JavaGlobalReference type) in _helpers)
        {
            type.Dispose();
            loader.Dispose();
        }

        _helpers.Clear();
        Log(message: "UNLOAD");
    }

    /// <inheritdoc/>
    public override void OnVmDeath(AgentContext context, JavaEnvironment environment)
    {
        Log(message: "VM_DEATH");
    }

    /// <inheritdoc/>
    public override void OnVmInit(AgentContext context, JavaEnvironment environment)
    {
        Log(message: "VM_INIT");
        InstallSystemHelper(environment);
        LoadedMarker.Mark(environment);

        if (context.Options.Contains(value: "fail-callback", StringComparison.Ordinal))
            throw new InvalidOperationException(message: "Intentional callback failure.");
    }

    /// <inheritdoc/>
    public override byte[]? TransformClass(AgentContext context, ClassFile file)
    {
        if (file.Name == CallbackClassFile.Name)
            return null;

        if (file.Name != "BridgeFixture")
            return null;

        // The fixture's unique constant has equal-length replacements. This is
        // intentionally an example, not a general Java bytecode rewriting API.
        byte[] patched = file.Bytes.ToArray();
        byte[] original = Encoding.ASCII.GetBytes(s: "ORIGINAL");
        byte[] replacement = Encoding.ASCII.GetBytes(file.IsRetransformation ? "RELOADED" : "MODIFIED");
        int index = patched.AsSpan().IndexOf(original);

        if (index < 0)
            throw new InvalidOperationException(message: "Fixture constant was not found.");

        replacement.CopyTo(patched, index);

        if (_helpers.Count > 0)
            patched = CallbackClassFile.Inject(patched);

        Log(file.IsRetransformation ? "RETRANSFORM" : "TRANSFORM");

        return patched;
    }

    /// <inheritdoc/>
    public override byte[]? TransformClass(AgentContext context, ClassFile file, ClassTransformContext callback)
    {
        if (file.Name == "java/util/concurrent/atomic/AtomicStampedReference" && callback.Loader == 0)
            Log(message: "BOOTSTRAP_CALLBACK_NULL_LOADER");

        if (file.Name != "BridgeFixture$Twin")
            return base.TransformClass(context, file, callback);

        if (!callback.CanUseJni || callback.Environment is not { } environment || _targetLoader is not { } targetLoader)
            throw new InvalidOperationException(message: "Twin transformation requires a live JNI environment and a selected loader.");

        if (callback.Loader == 0 || callback.ProtectionDomain == 0 || callback.ClassBeingRedefined != 0 != file.IsRetransformation)
            throw new InvalidOperationException(message: "Twin transformation received incomplete class metadata.");

        bool selected = environment.IsSameObject(callback.Loader, targetLoader.Handle);
        string expectedLocation = selected ? "file:/jvmbridge-selected.jar" : "file:/jvmbridge-other.jar";

        using JavaLocalReference domainType = environment.FindClass(name: "java/security/ProtectionDomain");

        nint getCodeSource = environment.GetMethod(domainType.Handle,
            name: "getCodeSource",
            signature: "()Ljava/security/CodeSource;");

        using JavaLocalReference codeSource = environment.CallObject(callback.ProtectionDomain, getCodeSource, arguments: []);

        if (codeSource.Handle == 0)
            throw new InvalidOperationException(message: "Twin protection domain has no code source.");

        using JavaLocalReference sourceType = environment.FindClass(name: "java/security/CodeSource");

        nint getLocation = environment.GetMethod(sourceType.Handle, name: "getLocation", signature: "()Ljava/net/URL;");

        using JavaLocalReference location = environment.CallObject(codeSource.Handle, getLocation, arguments: []);

        if (location.Handle == 0)
            throw new InvalidOperationException(message: "Twin code source has no location.");

        using JavaLocalReference urlType = environment.FindClass(name: "java/net/URL");

        nint toExternalForm = environment.GetMethod(urlType.Handle, name: "toExternalForm", signature: "()Ljava/lang/String;");

        using JavaLocalReference locationText = environment.CallObject(location.Handle, toExternalForm, arguments: []);

        if (environment.GetString(locationText.Handle) != expectedLocation)
            throw new InvalidOperationException(message: "Twin code source does not match its defining loader.");

        Log(selected ? "SELECTED_CODE_SOURCE" : "OTHER_CODE_SOURCE");

        if (!selected)
            return null;

        byte[] patched = file.Bytes.ToArray();
        int index = patched.AsSpan().IndexOf(Encoding.ASCII.GetBytes(s: "ORIGINAL"));

        if (index < 0)
            throw new InvalidOperationException(message: "Twin class constant was not found.");

        Encoding.ASCII.GetBytes(file.IsRetransformation ? "RELOADED" : "MODIFIED").CopyTo(patched, index);

        patched = CallbackClassFile.Inject(patched);

        Log(file.IsRetransformation ? "SELECTED_RETRANSFORM" : "SELECTED_TRANSFORM");

        return patched;
    }

    internal static JavaException CaptureJavaException(Action action)
    {
        try
        {
            action();
        }
        catch (JavaException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(message: "Expected a cleared Java exception.");
    }

    internal static bool ContainLoggingFailure(Exception exception)
    {
        GC.KeepAlive(exception);

        return true;
    }

    internal static void ExpectJavaException(Action action)
    {
        try
        {
            action();
        }
        catch (JavaException)
        {
            return;
        }

        throw new InvalidOperationException(message: "Expected a cleared Java exception.");
    }

    internal static void Log(string message)
    {
        try
        {
            Console.Error.WriteLine(value: $"JvmBridge sample: {message}");
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            return;
        }
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    [UnmanagedCallersOnly]
    private static void ClearTargetLoader(JNINativeInterface_** nativeEnvironment, _jobject* caller)
    {
        try
        {
            s_agent?._targetLoader?.Dispose();

            if (s_agent is { } agent)
                agent._targetLoader = null;
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log(message: $"NATIVE_ERROR {exception.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static void ObserveWeak(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* value)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            BridgeAgent agent = s_agent ?? throw new InvalidOperationException(message: "Agent is not initialized.");
            JavaWeakGlobalReference weak = environment.NewWeakGlobalReference((nint)value);

            try
            {
                using JavaLocalReference original = environment.NewLocalReference((nint)value);

                using JavaGlobalReference global = original.ToGlobal();

                JavaVirtualMachine machine = environment.GetVirtualMachine();
                Exception? failure = null;

                Thread worker = new(() =>
                {
                    try
                    {
                        using JavaThreadAttachment attachment = machine.AttachCurrentThread();

                        JavaEnvironment currentEnvironment = attachment.Environment;

                        using JavaLocalReference? promoted = weak.TryPromote(currentEnvironment)
                            ?? throw new InvalidOperationException(message: "Weak reference was collected while strongly reachable.");

                        using JavaLocalReference duplicate = currentEnvironment.NewLocalReference(global.Handle);

                        using JavaLocalReference different = currentEnvironment.NewString(value: "different");

                        Require(currentEnvironment.IsSameObject(promoted.Handle, duplicate.Handle),
                            message: "Separate handles lost Java identity.");

                        Require(!currentEnvironment.IsSameObject(promoted.Handle, different.Handle),
                            message: "Different objects share Java identity.");

                        Require(currentEnvironment.IsSameObject(first: 0, second: 0),
                            message: "Two Java nulls did not compare equal.");

                        Require(!currentEnvironment.IsSameObject(promoted.Handle, second: 0),
                            message: "A live object compared equal to Java null.");
                    }
                    catch (Exception exception) when (ContainLoggingFailure(exception))
                    {
                        failure = exception;
                    }
                });

                worker.Start();
                worker.Join();

                if (failure is not null)
                    throw failure;

                agent._observedWeak?.Dispose();

                agent._observedWeak = weak;
            }
            catch
            {
                weak.Dispose();

                throw;
            }
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log(message: $"NATIVE_ERROR {exception.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnFrame(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* client)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            if (Context?.Options.Contains(value: "fail-helper", StringComparison.Ordinal) == true)
                throw new InvalidOperationException(message: "Intentional helper callback failure.");

            if (client == null)
            {
                Log(message: "HELPER_NULL_OK");

                return;
            }

            _jobject* clientType = environment.Functions->GetObjectClass(nativeEnvironment, client);

            environment.ThrowIfException(nameof(OnFrame));

            if (clientType == null)
                throw new InvalidOperationException(message: "GetObjectClass returned null.");

            try
            {
                nint ownerMethod = environment.GetMethod((nint)clientType, name: "owner", signature: "()Ljava/lang/Thread;");

                using JavaLocalReference owner = environment.CallObject((nint)client, ownerMethod, arguments: []);

                using JavaLocalReference threadType = environment.FindClass(name: "java/lang/Thread");

                nint currentMethod = environment.GetMethod(threadType.Handle,
                    name: "currentThread",
                    signature: "()Ljava/lang/Thread;",
                    isStatic: true);

                using JavaLocalReference current = environment.CallObject(threadType.Handle,
                    currentMethod,
                    arguments: [],
                    isStatic: true);

                if (!environment.IsSameObject(owner.Handle, current.Handle))
                    throw new InvalidOperationException(message: "Helper callback crossed Java threads.");
            }
            finally
            {
                environment.Functions->DeleteLocalRef(nativeEnvironment, clientType);
            }

            Log(message: "HELPER_THREAD_OK");
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            if (exception.Message == "Intentional helper callback failure.")
                Log(message: "HELPER_CALLBACK_CONTAINED");
            else
                Log(message: $"NATIVE_ERROR {exception.Message}");
        }
    }

    private static void Register(JavaEnvironment environment, nint type)
    {
        environment.RegisterNative(type,
            name: "observeWeak",
            signature: "(Ljava/lang/Object;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&ObserveWeak);

        environment.RegisterNative(type,
            name: "weakCollected",
            signature: "()Z",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, byte>)&WeakCollected);

        environment.RegisterNative(type,
            name: "roundTrip",
            signature: "(Ljava/lang/String;)Ljava/lang/String;",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, _jobject*>)&RoundTrip);

        environment.RegisterNative(type,
            name: "retransform",
            signature: "(Ljava/lang/Class;)Z",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, byte>)&Retransform);

        environment.RegisterNative(type,
            name: "registerTargetLoader",
            signature: "(Ljava/lang/ClassLoader;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&RegisterTargetLoader);

        environment.RegisterNative(type,
            name: "registerHelperLoader",
            signature: "(Ljava/lang/ClassLoader;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&RegisterHelperLoader);

        environment.RegisterNative(type,
            name: "clearTargetLoader",
            signature: "()V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, void>)&ClearTargetLoader);

        environment.RegisterNative(type,
            name: "inspectMembers",
            signature: "(Ljava/lang/Class;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&MemberInspection.InspectMembers);

        environment.RegisterNative(type,
            name: "inspectTwinTypes",
            signature: "(Ljava/lang/Class;Ljava/lang/Class;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, _jobject*, void>)&MemberInspection.InspectTwinTypes);

        Log(message: "REGISTER_NATIVES");
    }

    [UnmanagedCallersOnly]
    private static void RegisterHelperLoader(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* loader)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            BridgeAgent agent = s_agent ?? throw new InvalidOperationException(message: "Agent is not active.");

            agent.InstallHelper(environment, (nint)loader);
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log(message: $"NATIVE_ERROR {exception.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static void RegisterTargetLoader(JNINativeInterface_** nativeEnvironment, _jobject* caller, _jobject* loader)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return;

            BridgeAgent agent = s_agent ?? throw new InvalidOperationException(message: "Agent is not active.");

            if (agent._targetLoader is not null)
                throw new InvalidOperationException(message: "Target loader is already registered.");

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            using JavaLocalReference local = environment.NewLocalReference((nint)loader);

            agent._targetLoader = local.ToGlobal();
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log(message: $"NATIVE_ERROR {exception.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static byte Retransform(JNINativeInterface_** environment, _jobject* caller, _jobject* type)
    {
        try
        {
            if (Context is not { CanRetransform: true } context)
                return 0;

            context.Retransform((nint)type);

            return 1;
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log(message: $"NATIVE_ERROR {exception.Message}");

            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static _jobject* RoundTrip(JNINativeInterface_** nativeEnvironment, _jobject* type, _jobject* value)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return null;

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            using JavaLocalReference local = environment.NewLocalReference((nint)value);

            using JavaGlobalReference global = local.ToGlobal();

            string? roundTripped = null;
            Exception? failure = null;
            JavaVirtualMachine machine = environment.GetVirtualMachine();

            Thread worker = new(() =>
            {
                try
                {
                    using JavaThreadAttachment attachment = machine.AttachCurrentThread();

                    roundTripped = attachment.Environment.GetString(global.Handle);
                }
                catch (Exception exception) when (ContainLoggingFailure(exception))
                {
                    failure = exception;
                }
            });

            worker.Start();
            worker.Join();

            if (failure is not null)
                throw failure;

            using JavaLocalReference converted = environment.NewString(roundTripped ?? string.Empty);

            return environment.Functions->NewLocalRef(nativeEnvironment, (_jobject*)converted.Handle);
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log(message: $"NATIVE_ERROR {exception.Message}");

            return null;
        }
    }

    [UnmanagedCallersOnly]
    private static byte WeakCollected(JNINativeInterface_** nativeEnvironment, _jobject* caller)
    {
        try
        {
            if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                return 0;

            using JavaEnvironment environment = new((nint)nativeEnvironment);

            JavaWeakGlobalReference weak = s_agent?._observedWeak
                ?? throw new InvalidOperationException(message: "No weak reference was observed.");

            using JavaLocalReference? promoted = weak.TryPromote(environment);

            return promoted is null ? (byte)1 : (byte)0;
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log(message: $"NATIVE_ERROR {exception.Message}");

            return 0;
        }
    }

    private void InstallHelper(JavaEnvironment environment, nint loader)
    {
        ArgumentOutOfRangeException.ThrowIfZero(loader);

        if (_helpers.Any(helper => environment.IsSameObject(helper.Loader.Handle, loader)))
            return;

        byte[] bytes = CallbackClassFile.Generate();

        using JavaLocalReference helper = environment.DefineClass(CallbackClassFile.Name, loader, bytes);

        environment.RegisterNative(helper.Handle,
            name: "onFrame",
            signature: "(Ljava/lang/Object;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&OnFrame);

        using JavaLocalReference localLoader = environment.NewLocalReference(loader);

        JavaGlobalReference globalLoader = localLoader.ToGlobal();

        try
        {
            _helpers.Add((globalLoader, helper.ToGlobal()));
        }
        catch
        {
            globalLoader.Dispose();

            throw;
        }

        Log(message: "HELPER_INSTALLED");

        try
        {
            using JavaLocalReference duplicate = environment.DefineClass(CallbackClassFile.Name, loader, bytes);

            throw new InvalidOperationException(message: "Duplicate helper class definition unexpectedly succeeded.");
        }
        catch (JavaException exception) when (exception.Operation == nameof(JavaEnvironment.DefineClass))
        {
            Log(message: "HELPER_DUPLICATE_REJECTED");
        }
    }

    private void InstallSystemHelper(JavaEnvironment environment)
    {
        using JavaLocalReference loaderType = environment.FindClass(name: "java/lang/ClassLoader");

        nint method = environment.GetMethod(loaderType.Handle,
            name: "getSystemClassLoader",
            signature: "()Ljava/lang/ClassLoader;",
            isStatic: true);

        using JavaLocalReference loader = environment.CallObject(loaderType.Handle, method, arguments: [], isStatic: true);

        InstallHelper(environment, loader.Handle);
    }
}

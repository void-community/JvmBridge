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
    private static AgentContext? s_context;
    private JavaGlobalReference? _targetLoader;

    /// <inheritdoc/>
    public override void Configure(AgentContext context)
    {
        if (!context.TryEnableRetransformation())
            Log(message: "RETRANSFORM_UNAVAILABLE");
    }

    /// <inheritdoc/>
    public override void OnAttach(AgentContext context)
    {
        OnLoad(context);
        Log(message: "ATTACH");

        using JavaThreadAttachment attachment = context.VirtualMachine.AttachCurrentThread();

        JavaEnvironment environment = attachment.Environment;
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
    public override void OnClassPrepare(AgentContext context, JavaEnvironment environment, nint type)
    {
        if (context.GetClassSignature(type) == "LBridgeFixture;")
            Register(environment, type);
    }

    /// <inheritdoc/>
    public override void OnLoad(AgentContext context)
    {
        s_context = context;
        s_agent = this;

        if (context.Options.Contains(value: "fail-start", StringComparison.Ordinal))
            throw new InvalidOperationException(message: "Intentional initialization failure.");

        Log($"LOAD retransform={context.CanRetransform}");
    }

    /// <inheritdoc/>
    public override void OnUnload(AgentContext context)
    {
        _targetLoader?.Dispose();
        _targetLoader = null;
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

        using JavaLocalReference system = environment.FindClass(name: "java/lang/System");

        nint method = environment.GetMethod(system.Handle, name: "setProperty", signature: "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", isStatic: true);

        using JavaLocalReference name = environment.NewString(value: "jvmbridge.loaded");

        using JavaLocalReference value = environment.NewString(value: "true");

        jvalue[] arguments = [new() { l = (_jobject*)name.Handle }, new() { l = (_jobject*)value.Handle }];

        using JavaLocalReference previous = environment.CallObject(system.Handle, method, arguments, isStatic: true);

        if (context.Options.Contains(value: "fail-callback", StringComparison.Ordinal))
            throw new InvalidOperationException(message: "Intentional callback failure.");
    }

    /// <inheritdoc/>
    public override byte[]? TransformClass(AgentContext context, ClassFile file)
    {
        if (file.Name != "BridgeFixture")
            return null;

        // The fixture's unique constant has equal-length replacements. This is
        // intentionally an example, not a general Java bytecode rewriting API.
        byte[] result = file.Bytes.ToArray();
        byte[] original = Encoding.ASCII.GetBytes(s: "ORIGINAL");
        byte[] replacement = Encoding.ASCII.GetBytes(file.IsRetransformation ? "RELOADED" : "MODIFIED");
        int index = result.AsSpan().IndexOf(original);

        if (index < 0)
            throw new InvalidOperationException(message: "Fixture constant was not found.");

        replacement.CopyTo(result, index);
        Log(file.IsRetransformation ? "RETRANSFORM" : "TRANSFORM");

        return result;
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

        nint getCodeSource = environment.GetMethod(domainType.Handle, name: "getCodeSource", signature: "()Ljava/security/CodeSource;");

        using JavaLocalReference codeSource = environment.CallObject(callback.ProtectionDomain, getCodeSource, []);

        if (codeSource.Handle == 0)
            throw new InvalidOperationException(message: "Twin protection domain has no code source.");

        using JavaLocalReference sourceType = environment.FindClass(name: "java/security/CodeSource");

        nint getLocation = environment.GetMethod(sourceType.Handle, name: "getLocation", signature: "()Ljava/net/URL;");

        using JavaLocalReference location = environment.CallObject(codeSource.Handle, getLocation, []);

        if (location.Handle == 0)
            throw new InvalidOperationException(message: "Twin code source has no location.");

        using JavaLocalReference urlType = environment.FindClass(name: "java/net/URL");

        nint toExternalForm = environment.GetMethod(urlType.Handle, name: "toExternalForm", signature: "()Ljava/lang/String;");

        using JavaLocalReference locationText = environment.CallObject(location.Handle, toExternalForm, []);

        if (environment.GetString(locationText.Handle) != expectedLocation)
            throw new InvalidOperationException(message: "Twin code source does not match its defining loader.");

        Log(selected ? "SELECTED_CODE_SOURCE" : "OTHER_CODE_SOURCE");

        if (!selected)
            return null;

        byte[] result = file.Bytes.ToArray();
        int index = result.AsSpan().IndexOf(Encoding.ASCII.GetBytes(s: "ORIGINAL"));

        if (index < 0)
            throw new InvalidOperationException(message: "Twin class constant was not found.");

        Encoding.ASCII.GetBytes(file.IsRetransformation ? "RELOADED" : "MODIFIED").CopyTo(result, index);
        Log(file.IsRetransformation ? "SELECTED_RETRANSFORM" : "SELECTED_TRANSFORM");

        return result;
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
        catch (Exception exception) when (ContainLoggingFailure(exception)) { Log($"NATIVE_ERROR {exception.Message}"); }
    }

    private static bool ContainLoggingFailure(Exception exception)
    {
        GC.KeepAlive(exception);

        return true;
    }

    private static void Log(string message)
    {
        try
        {
            Console.Error.WriteLine($"JvmBridge sample: {message}");
        }
        catch (Exception exception) when (ContainLoggingFailure(exception)) { return; }
    }

    private static void Register(JavaEnvironment environment, nint type)
    {
        environment.RegisterNative(
            type,
            name: "roundTrip",
            signature: "(Ljava/lang/String;)Ljava/lang/String;",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, _jobject*>)&RoundTrip
        );
        environment.RegisterNative(
            type,
            name: "retransform",
            signature: "(Ljava/lang/Class;)Z",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, byte>)&Retransform
        );
        environment.RegisterNative(
            type,
            name: "registerTargetLoader",
            signature: "(Ljava/lang/ClassLoader;)V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, void>)&RegisterTargetLoader
        );
        environment.RegisterNative(
            type,
            name: "clearTargetLoader",
            signature: "()V",
            (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, void>)&ClearTargetLoader
        );
        Log(message: "REGISTER_NATIVES");
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
        catch (Exception exception) when (ContainLoggingFailure(exception)) { Log($"NATIVE_ERROR {exception.Message}"); }
    }

    [UnmanagedCallersOnly]
    private static byte Retransform(JNINativeInterface_** environment, _jobject* caller, _jobject* type)
    {
        try
        {
            if (s_context is not { CanRetransform: true } context)
                return 0;

            context.Retransform((nint)type);

            return 1;
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log($"NATIVE_ERROR {exception.Message}");

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

            string? result = null;
            Exception? failure = null;
            JavaVirtualMachine machine = environment.GetVirtualMachine();

            Thread worker = new(
                () =>
            {
                try
                {
                    using JavaThreadAttachment attachment = machine.AttachCurrentThread();

                    result = attachment.Environment.GetString(global.Handle);
                }
                catch (Exception exception) when (ContainLoggingFailure(exception)) { failure = exception; }
            }
            );

            worker.Start();
            worker.Join();

            if (failure is not null)
                throw failure;

            using JavaLocalReference converted = environment.NewString(result ?? string.Empty);

            return environment.Functions->NewLocalRef(nativeEnvironment, (_jobject*)converted.Handle);
        }
        catch (Exception exception) when (ContainLoggingFailure(exception))
        {
            Log($"NATIVE_ERROR {exception.Message}");

            return null;
        }
    }
}

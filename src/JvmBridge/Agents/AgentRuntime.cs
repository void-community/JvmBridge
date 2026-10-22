using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using JvmBridge.Native;
using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>Lifecycle dispatch used by generated exports. Exceptions never escape native callbacks.</summary>
public static unsafe class AgentRuntime
{
    private static readonly ConcurrentDictionary<nint, AgentContext> _contexts = new();

    /// <summary>Starts an agent from a generated native load or attach entry point.</summary>
    /// <param name="factory">Creates the configured agent instance.</param>
    /// <param name="machine">The raw JVM invocation-interface pointer.</param>
    /// <param name="options">The nullable modified UTF-8 options pointer supplied by the JVM.</param>
    /// <param name="attached">Whether the entry point represents late attachment.</param>
    /// <param name="entryPoint">An address inside the NativeAOT module that must remain loaded.</param>
    /// <param name="agentName">The name that prefixes the agent's log lines, which generated entry points set to the assembly name. Reading it from reflection would initialize globalization, which aborts the process on hosts without libicu.</param>
    /// <returns>The JNI success or failure result for the native entry point.</returns>
    public static int Start(Func<JavaAgent> factory, nint machine, nint options, bool attached, nint entryPoint = 0, string? agentName = null)
    {
        // A library that is loaded again into a JVM where its agent already runs is a command for that agent, not a second agent.
        if (attached && FindRunning(machine) is { } running)
            return RunCommand(running, ModifiedUtf8.Decode((byte*)options));

        AgentContext? context = null;
        nint registeredHandle = 0;
        try
        {
            if (entryPoint != 0)
                NativeModule.Retain(entryPoint);
            context = new AgentContext(factory(), machine, ModifiedUtf8.Decode((byte*)options), attached, agentName);
            if (!_contexts.TryAdd(context.RawHandle, context))
                throw new InvalidOperationException("An agent is already registered for this tooling environment.");
            registeredHandle = context.RawHandle;
            context.Agent.Configure(context);
            var environment = (jvmtiInterface_1_**)context.RawHandle;
            jvmtiEventCallbacks callbacks = default;
            callbacks.VMInit = &VmInit;
            callbacks.VMDeath = &VmDeath;
            callbacks.ClassPrepare = &ClassPrepare;
            callbacks.ClassFileLoadHook = &ClassFileLoad;
            // SetEventCallbacks accepts a prefix; do not read Java 9+ callback slots on Java 8.
            int size = (int)Marshal.OffsetOf<jvmtiEventCallbacks>(nameof(jvmtiEventCallbacks.SampledObjectAlloc));
            // An agent that attaches later is expected to patch classes that are already loaded, and the capability can only be added before events are enabled.
            if (attached && !context.CanRetransform)
                _ = context.TryEnableRetransformation();
            AgentContext.Check((*environment)->SetEventCallbacks(environment, &callbacks, size), "SetEventCallbacks");
            foreach (jvmtiEvent notification in new[] { jvmtiEvent.JVMTI_EVENT_VM_INIT, jvmtiEvent.JVMTI_EVENT_VM_DEATH, jvmtiEvent.JVMTI_EVENT_CLASS_PREPARE, jvmtiEvent.JVMTI_EVENT_CLASS_FILE_LOAD_HOOK })
                AgentContext.Check(((delegate* unmanaged[Cdecl]<jvmtiInterface_1_**, jvmtiEventMode, jvmtiEvent, _jobject*, jvmtiError>)(*environment)->SetEventNotificationMode)(environment, jvmtiEventMode.JVMTI_ENABLE, notification, null), "SetEventNotificationMode");
            context.EventsEnabled = true;
            if (attached)
            {
                StartPatching(context);
                context.Agent.OnAttach(context);
            }
            else
                context.Agent.OnLoad(context);
            return Methods.JNI_OK;
        }
        catch (Exception exception) when (ReportAndContain(exception))
        {
            if (context is not null)
            {
                AgentContext cleanupContext = context;

                if (registeredHandle != 0 && _contexts.TryRemove(registeredHandle, out AgentContext? registeredContext))
                    cleanupContext = registeredContext;

                DisposeContext(cleanupContext);
            }

            return Methods.JNI_ERR;
        }
    }

    /// <summary>Performs non-throwing logical cleanup for agents owned by a JVM during shutdown.</summary>
    /// <param name="machine">The raw JVM invocation-interface pointer.</param>
    public static void Stop(nint machine)
    {
        try { StopCore(machine); }
        catch (Exception exception) when (ReportAndContain(exception)) { return; }
    }

    private static AgentContext? FindRunning(nint machine)
    {
        return _contexts.Values.FirstOrDefault(candidate => candidate.VirtualMachine.Handle == machine);
    }

    private static int RunCommand(AgentContext context, string command)
    {
        try
        {
            context.Agent.OnCommand(context, command);

            return Methods.JNI_OK;
        }
        catch (Exception exception) when (ReportAndContain(exception))
        {
            return Methods.JNI_ERR;
        }
    }

    private static void StartPatching(AgentContext context)
    {
        try { context.Patches.OnAttach(); }
        catch (Exception exception) when (ReportAndContain(exception)) { return; }
    }

    private static void DisposeContext(AgentContext context)
    {
        try { context.Dispose(); }
        catch (Exception exception) when (ReportAndContain(exception)) { return; }
    }

    /// <summary>Reports an exception that a native method contained, for use in an exception filter that returns a default value to the JVM.</summary>
    /// <param name="exception">The exception to report.</param>
    /// <returns>Always <see langword="true"/>, so the filter contains the exception.</returns>
    public static bool ReportFailure(Exception exception) => ReportAndContain(exception);

    private static bool ReportAndContain(Exception exception)
    {
        AgentLog.Write(AgentLogLevel.Error, "JvmBridge", $"{exception.GetType().Name}: {exception.Message}");

        return true;
    }

    private static void StopCore(nint machine)
    {
        foreach (KeyValuePair<nint, AgentContext> entry in _contexts)
        {
            if (entry.Value.VirtualMachine.Handle != machine || !_contexts.TryRemove(entry.Key, out AgentContext? context))
                continue;
            try { context.Agent.OnUnload(context); }
            catch (Exception exception) when (ReportAndContain(exception)) { continue; }
            finally { context.Patches.Cleanup(); }
            // JVM shutdown owns native environment disposal. NativeAOT libraries stay loaded until process exit.
        }
    }

    [UnmanagedCallersOnly]
    private static void VmInit(jvmtiInterface_1_** tooling, JNINativeInterface_** nativeEnvironment, _jobject* thread)
    {
        try
        {
            if (_contexts.TryGetValue((nint)tooling, out AgentContext? context))
            {
                // A JVM may enter a tooling callback while a checked JNI call is
                // still in progress. Acknowledge its check flag without clearing
                // an actual pending Java exception.
                if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                    return;
                using JavaEnvironment environment = new((nint)nativeEnvironment);
                context.Patches.OnVmInit(environment);
                context.Agent.OnVmInit(context, environment);
            }
        }
        catch (Exception exception) when (ReportAndContain(exception)) { return; }
    }

    [UnmanagedCallersOnly]
    private static void VmDeath(jvmtiInterface_1_** tooling, JNINativeInterface_** nativeEnvironment)
    {
        try
        {
            if (_contexts.TryGetValue((nint)tooling, out AgentContext? context))
            {
                // A JVM may enter a tooling callback while a checked JNI call is
                // still in progress. Acknowledge its check flag without clearing
                // an actual pending Java exception.
                if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                    return;
                using JavaEnvironment environment = new((nint)nativeEnvironment);
                try { context.Agent.OnVmDeath(context, environment); }
                finally { Stop(context.VirtualMachine.Handle); }
            }
        }
        catch (Exception exception) when (ReportAndContain(exception)) { return; }
    }

    [UnmanagedCallersOnly]
    private static void ClassPrepare(jvmtiInterface_1_** tooling, JNINativeInterface_** nativeEnvironment, _jobject* thread, _jobject* type)
    {
        try
        {
            if (_contexts.TryGetValue((nint)tooling, out AgentContext? context))
            {
                // A JVM may enter a tooling callback while a checked JNI call is
                // still in progress. Acknowledge its check flag without clearing
                // an actual pending Java exception.
                if ((*nativeEnvironment)->ExceptionCheck(nativeEnvironment) != 0)
                    return;
                using JavaEnvironment environment = new((nint)nativeEnvironment);
                context.Agent.OnClassPrepare(context, environment, (nint)type);
            }
        }
        catch (Exception exception) when (ReportAndContain(exception)) { return; }
    }

    [UnmanagedCallersOnly]
    private static void ClassFileLoad(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, _jobject* redefined, _jobject* loader, byte* name, _jobject* protectionDomain, int length, byte* bytes, int* replacementLength, byte** replacement)
    {
        try
        {
            if (!_contexts.TryGetValue((nint)tooling, out AgentContext? context))
                return;
            jvmtiPhase phase = default;
            AgentContext.Check((*tooling)->GetPhase(tooling, &phase), "GetPhase");
            bool canUseJni = environment != null && phase is jvmtiPhase.JVMTI_PHASE_START or jvmtiPhase.JVMTI_PHASE_LIVE;
            if (canUseJni && (*environment)->ExceptionCheck(environment) != 0)
                return;
            using JavaEnvironment? javaEnvironment = environment == null ? null : new JavaEnvironment((nint)environment);
            ClassFile file = new(name == null ? null : ModifiedUtf8.Decode(name), redefined != null, new ReadOnlySpan<byte>(bytes, length));
            ClassTransformContext callback = new(javaEnvironment, (nint)loader, (nint)protectionDomain, (nint)redefined, canUseJni);
            byte[]? patched = context.Patches.Transform(file, callback);
            ClassFile effective = patched is null ? file : new ClassFile(file.Name, file.IsRetransformation, patched);
            byte[]? result = context.Agent.TransformClass(context, effective, callback) ?? patched;
            if (result is null)
                return;
            byte* output = null;
            AgentContext.Check((*tooling)->Allocate(tooling, result.Length, &output), "Allocate transformed class");
            result.CopyTo(new Span<byte>(output, result.Length));
            *replacementLength = result.Length;
            *replacement = output;
        }
        catch (Exception exception) when (ReportAndContain(exception)) { return; }
    }
}

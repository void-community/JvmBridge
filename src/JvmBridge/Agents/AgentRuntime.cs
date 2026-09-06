using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using JvmBridge.Native;
using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>Lifecycle dispatch used by generated exports. Exceptions never escape native callbacks.</summary>
public static unsafe class AgentRuntime
{
    private static readonly ConcurrentDictionary<nint, AgentContext> _contexts = new();

    public static int Start(Func<JavaAgent> factory, nint machine, nint options, bool attached, nint entryPoint = 0)
    {
        AgentContext? context = null;
        try
        {
            if (entryPoint != 0)
                NativeModule.Retain(entryPoint);
            context = new AgentContext(factory(), machine, ModifiedUtf8.Decode((byte*)options), attached);
            if (!_contexts.TryAdd(context.RawHandle, context))
                throw new InvalidOperationException("An agent is already registered for this tooling environment.");
            if (attached)
                context.Agent.OnAttach(context);
            else
                context.Agent.OnLoad(context);
            var environment = (jvmtiInterface_1_**)context.RawHandle;
            jvmtiEventCallbacks callbacks = default;
            callbacks.VMInit = &VmInit;
            callbacks.VMDeath = &VmDeath;
            callbacks.ClassPrepare = &ClassPrepare;
            callbacks.ClassFileLoadHook = &ClassFileLoad;
            // SetEventCallbacks accepts a prefix; do not read Java 9+ callback slots on Java 8.
            int size = (int)Marshal.OffsetOf<jvmtiEventCallbacks>(nameof(jvmtiEventCallbacks.SampledObjectAlloc));
            AgentContext.Check((*environment)->SetEventCallbacks(environment, &callbacks, size), "SetEventCallbacks");
            foreach (jvmtiEvent notification in new[] { jvmtiEvent.JVMTI_EVENT_VM_INIT, jvmtiEvent.JVMTI_EVENT_VM_DEATH, jvmtiEvent.JVMTI_EVENT_CLASS_PREPARE, jvmtiEvent.JVMTI_EVENT_CLASS_FILE_LOAD_HOOK })
                AgentContext.Check(((delegate* unmanaged[Cdecl]<jvmtiInterface_1_**, jvmtiEventMode, jvmtiEvent, _jobject*, jvmtiError>)(*environment)->SetEventNotificationMode)(environment, jvmtiEventMode.JVMTI_ENABLE, notification, null), "SetEventNotificationMode");
            context.EventsEnabled = true;
            return Methods.JNI_OK;
        }
        catch (Exception exception)
        {
            Report(exception);
            if (context is not null)
            {
                _contexts.TryRemove(context.RawHandle, out _);
                try { context.Dispose(); }
                catch (Exception cleanupException) { Report(cleanupException); }
            }
            return Methods.JNI_ERR;
        }
    }

    public static void Stop(nint machine)
    {
        try { StopCore(machine); }
        catch (Exception exception) { Report(exception); }
    }

    private static void StopCore(nint machine)
    {
        foreach (KeyValuePair<nint, AgentContext> entry in _contexts)
        {
            if (entry.Value.VirtualMachine.Handle != machine || !_contexts.TryRemove(entry.Key, out AgentContext? context))
                continue;
            try { context.Agent.OnUnload(context); }
            catch (Exception exception) { Report(exception); }
            // JVM shutdown owns native environment disposal. NativeAOT libraries stay loaded until process exit.
        }
    }

    private static void Report(Exception exception)
    {
        try { Console.Error.WriteLine($"JvmBridge: {exception.GetType().Name}: {exception.Message}"); }
        catch { /* The unmanaged boundary must remain non-throwing even if stderr is unavailable. */ }
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
                context.Agent.OnVmInit(context, environment);
            }
        }
        catch (Exception exception) { Report(exception); }
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
                context.Agent.OnVmDeath(context, environment);
            }
        }
        catch (Exception exception) { Report(exception); }
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
        catch (Exception exception) { Report(exception); }
    }

    [UnmanagedCallersOnly]
    private static void ClassFileLoad(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, _jobject* redefined, _jobject* loader, byte* name, _jobject* protectionDomain, int length, byte* bytes, int* replacementLength, byte** replacement)
    {
        try
        {
            if (!_contexts.TryGetValue((nint)tooling, out AgentContext? context))
                return;
            ClassFile file = new(name == null ? null : ModifiedUtf8.Decode(name), redefined != null, new ReadOnlySpan<byte>(bytes, length));
            byte[]? result = context.Agent.TransformClass(context, file);
            if (result is null)
                return;
            byte* output = null;
            AgentContext.Check((*tooling)->Allocate(tooling, result.Length, &output), "Allocate transformed class");
            result.CopyTo(new Span<byte>(output, result.Length));
            *replacementLength = result.Length;
            *replacement = output;
        }
        catch (Exception exception) { Report(exception); }
    }
}

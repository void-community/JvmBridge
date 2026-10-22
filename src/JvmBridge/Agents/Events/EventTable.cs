using System.Runtime.InteropServices;

using JvmBridge.Native;

namespace JvmBridge.Agents;

/// <summary>
/// The optional entries of the one JVMTI callback table that JvmBridge installs. Every entry has a trampoline from the first, and an event only reaches it once an agent subscribes, so the table is built once and never replaced.
/// Each trampoline contains the exceptions of the handler and counts failures in <see cref="AgentEvents"/>.
/// </summary>
internal static unsafe class EventTable
{
    /// <summary>Sets the callbacks of the events an agent can subscribe to. The four callbacks that always run are set by the caller.</summary>
    internal static void Compose(ref jvmtiEventCallbacks callbacks)
    {
        callbacks.ThreadStart = &ThreadStart;
        callbacks.ThreadEnd = &ThreadEnd;
        callbacks.ClassLoad = &ClassLoad;
        callbacks.Exception = &Exception;
        callbacks.MethodEntry = &MethodEntry;
        callbacks.MethodExit = &MethodExit;
        callbacks.GarbageCollectionStart = &GarbageCollectionStart;
        callbacks.GarbageCollectionFinish = &GarbageCollectionFinish;
        callbacks.MonitorContendedEnter = &MonitorContendedEnter;
        callbacks.MonitorContendedEntered = &MonitorContendedEntered;
    }

    [UnmanagedCallersOnly]
    private static void ClassLoad(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, _jobject* thread, _jobject* type)
    {
        if (!Enter(tooling, environment, out AgentContext? context))
            return;

        try
        {
            context.Agent.OnClassLoad(context, ThreadEnvironment.For((nint)environment), (nint)thread, (nint)type);
            context.Events.Succeeded(JavaEvents.ClassLoad);
        }
        catch (Exception exception) when (context.Events.Failed(JavaEvents.ClassLoad, exception))
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void Exception(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, _jobject* thread, _jmethodID* method, long location, _jobject* exception, _jmethodID* catchMethod, long catchLocation)
    {
        // The exception being thrown is pending while this event runs, so the pending-exception guard of the other events does not apply.
        AgentContext? context = AgentRuntime.FindContext((nint)tooling);

        if (context is null)
            return;

        try
        {
            JavaExceptionEvent thrown = new((nint)thread, (nint)method, location, (nint)exception, (nint)catchMethod, catchLocation);

            context.Agent.OnException(context, ThreadEnvironment.For((nint)environment), thrown);
            context.Events.Succeeded(JavaEvents.Exception);
        }
        catch (Exception failure) when (context.Events.Failed(JavaEvents.Exception, failure))
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void GarbageCollectionFinish(jvmtiInterface_1_** tooling)
    {
        AgentContext? context = AgentRuntime.FindContext((nint)tooling);

        if (context is null)
            return;

        try
        {
            context.Agent.OnGarbageCollectionFinish(context);
            context.Events.Succeeded(JavaEvents.GarbageCollectionFinish);
        }
        catch (Exception exception) when (context.Events.Failed(JavaEvents.GarbageCollectionFinish, exception))
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void GarbageCollectionStart(jvmtiInterface_1_** tooling)
    {
        AgentContext? context = AgentRuntime.FindContext((nint)tooling);

        if (context is null)
            return;

        try
        {
            context.Agent.OnGarbageCollectionStart(context);
            context.Events.Succeeded(JavaEvents.GarbageCollectionStart);
        }
        catch (Exception exception) when (context.Events.Failed(JavaEvents.GarbageCollectionStart, exception))
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MethodEntry(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, _jobject* thread, _jmethodID* method)
    {
        if (!Enter(tooling, environment, out AgentContext? context))
            return;

        try
        {
            context.Agent.OnMethodEntry(context, ThreadEnvironment.For((nint)environment), (nint)thread, (nint)method);
            context.Events.Succeeded(JavaEvents.MethodEntry);
        }
        catch (Exception exception) when (context.Events.Failed(JavaEvents.MethodEntry, exception))
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MethodExit(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, _jobject* thread, _jmethodID* method, byte poppedByException, jvalue returnValue)
    {
        AgentContext? context = AgentRuntime.FindContext((nint)tooling);

        // A method left by an exception leaves that exception pending, so the guard only applies to normal returns.
        if (context is null || (poppedByException == 0 && (*environment)->ExceptionCheck(environment) != 0))
            return;

        try
        {
            context.Agent.OnMethodExit(context, ThreadEnvironment.For((nint)environment), (nint)thread, (nint)method, poppedByException != 0);
            context.Events.Succeeded(JavaEvents.MethodExit);
        }
        catch (Exception exception) when (context.Events.Failed(JavaEvents.MethodExit, exception))
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MonitorContendedEnter(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, _jobject* thread, _jobject* monitor)
    {
        if (!Enter(tooling, environment, out AgentContext? context))
            return;

        try
        {
            context.Agent.OnMonitorContendedEnter(context, ThreadEnvironment.For((nint)environment), (nint)thread, (nint)monitor);
            context.Events.Succeeded(JavaEvents.MonitorContendedEnter);
        }
        catch (Exception exception) when (context.Events.Failed(JavaEvents.MonitorContendedEnter, exception))
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MonitorContendedEntered(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, _jobject* thread, _jobject* monitor)
    {
        if (!Enter(tooling, environment, out AgentContext? context))
            return;

        try
        {
            context.Agent.OnMonitorContendedEntered(context, ThreadEnvironment.For((nint)environment), (nint)thread, (nint)monitor);
            context.Events.Succeeded(JavaEvents.MonitorContendedEntered);
        }
        catch (Exception exception) when (context.Events.Failed(JavaEvents.MonitorContendedEntered, exception))
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void ThreadEnd(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, _jobject* thread)
    {
        if (!Enter(tooling, environment, out AgentContext? context))
            return;

        try
        {
            context.Agent.OnThreadEnd(context, ThreadEnvironment.For((nint)environment), (nint)thread);
            context.Events.Succeeded(JavaEvents.ThreadEnd);
        }
        catch (Exception exception) when (context.Events.Failed(JavaEvents.ThreadEnd, exception))
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void ThreadStart(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, _jobject* thread)
    {
        if (!Enter(tooling, environment, out AgentContext? context))
            return;

        try
        {
            context.Agent.OnThreadStart(context, ThreadEnvironment.For((nint)environment), (nint)thread);
            context.Events.Succeeded(JavaEvents.ThreadStart);
        }
        catch (Exception exception) when (context.Events.Failed(JavaEvents.ThreadStart, exception))
        {
        }
    }

    /// <summary>Finds the context of a callback and applies the guard the other callbacks use: a JVM may enter a tooling callback while a Java exception is pending, and ordinary JNI must not run then.</summary>
    private static bool Enter(jvmtiInterface_1_** tooling, JNINativeInterface_** environment, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AgentContext? context)
    {
        context = AgentRuntime.FindContext((nint)tooling);

        return context is not null && (*environment)->ExceptionCheck(environment) == 0;
    }
}

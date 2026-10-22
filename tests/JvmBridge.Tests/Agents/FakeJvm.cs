using System.Runtime.InteropServices;

using JvmBridge.Agents;
using JvmBridge.Native;

namespace JvmBridge.Tests.Agents;

/// <summary>
/// A JVM that has just enough of a tooling interface to create an <see cref="AgentContext"/> and to start an agent, for tests of agent logic that never calls into Java. It offers the capabilities a test asks for and records the callbacks
/// and the events the agent enables. Its state is static because the JVM calls back through function pointers, so every test that uses it belongs to <see cref="CollectionName"/> and runs one at a time.
/// </summary>
internal sealed unsafe class FakeJvm : IDisposable
{
    /// <summary>The xUnit collection of every test class that uses a fake JVM or replaces the agent log sink.</summary>
    internal const string CollectionName = "Agent tests";

    private static readonly List<EventToggle> RecordedToggles = [];
    private static nint s_currentTooling;
    private static jvmtiEventCallbacks s_installedCallbacks;
    private static int s_offeredCapabilities;
    private static int s_reportedThreadState;

    private readonly JNIInvokeInterface_** _machine;
    private readonly jvmtiInterface_1_** _tooling;

    internal FakeJvm()
    {
        RecordedToggles.Clear();
        s_offeredCapabilities = 0;
        s_reportedThreadState = 0;
        s_installedCallbacks = default;
        jvmtiInterface_1_* toolingTable = (jvmtiInterface_1_*)NativeMemory.AllocZeroed((nuint)sizeof(jvmtiInterface_1_));
        toolingTable->GetVersionNumber = &Version;
        toolingTable->GetPotentialCapabilities = &PotentialCapabilities;
        toolingTable->AddCapabilities = &AddCapabilities;
        toolingTable->GetThreadState = &ThreadState;
        toolingTable->SetEventCallbacks = &AcceptCallbacks;
        toolingTable->SetEventNotificationMode = (nint)(delegate* unmanaged[Cdecl]<jvmtiInterface_1_**, jvmtiEventMode, jvmtiEvent, _jobject*, jvmtiError>)&AcceptMode;
        _tooling = (jvmtiInterface_1_**)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        *_tooling = toolingTable;
        JNIInvokeInterface_* machineTable = (JNIInvokeInterface_*)NativeMemory.AllocZeroed((nuint)sizeof(JNIInvokeInterface_));
        machineTable->GetEnv = &GetEnvironment;
        _machine = (JNIInvokeInterface_**)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        *_machine = machineTable;
        s_currentTooling = (nint)_tooling;
    }

    /// <summary>Gets the callbacks that the agent installed with <c>SetEventCallbacks</c>.</summary>
    internal static jvmtiEventCallbacks Callbacks => s_installedCallbacks;

    /// <summary>Gets the event notifications that the agent enabled or disabled, in order.</summary>
    internal static IReadOnlyList<EventToggle> Toggles => RecordedToggles;

    /// <summary>Gets the pointer that stands for the JVM, as the JVM passes to <c>Agent_OnLoad</c>.</summary>
    internal nint Machine => (nint)_machine;

    public void Dispose()
    {
        AgentRuntime.Stop(Machine);
        s_currentTooling = 0;
        NativeMemory.Free(*_tooling);
        NativeMemory.Free(_tooling);
        NativeMemory.Free(*_machine);
        NativeMemory.Free(_machine);
    }

    /// <summary>Makes the JVM offer capabilities, as a JVM in a phase that allows them does.</summary>
    internal static void Offer(params JavaCapability[] capabilities)
    {
        s_offeredCapabilities = 0;

        foreach (JavaCapability capability in capabilities)
            s_offeredCapabilities |= 1 << (int)capability;
    }

    /// <summary>Sets the state bits that <c>GetThreadState</c> reports.</summary>
    internal static void ReportThreadState(JavaThreadState state)
    {
        s_reportedThreadState = (int)state;
    }

    /// <summary>Creates a context that has not enabled events, as one inside Configure.</summary>
    internal AgentContext CreateContext(string name, string options = "")
    {
        return new AgentContext(new EmptyAgent(), (nint)_machine, options, attached: false, name);
    }

    [UnmanagedCallersOnly]
    private static jvmtiError AcceptCallbacks(jvmtiInterface_1_** environment, jvmtiEventCallbacks* callbacks, int size)
    {
        s_installedCallbacks = *callbacks;

        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static jvmtiError AcceptMode(jvmtiInterface_1_** environment, jvmtiEventMode mode, jvmtiEvent notification, _jobject* thread)
    {
        RecordedToggles.Add(new EventToggle(notification, mode == jvmtiEventMode.JVMTI_ENABLE));

        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError AddCapabilities(jvmtiInterface_1_** environment, jvmtiCapabilities* capabilities)
    {
        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly]
    private static int GetEnvironment(JNIInvokeInterface_** machine, void** environment, int version)
    {
        *environment = (void*)s_currentTooling;

        return Methods.JNI_OK;
    }

    private static bool Offered(JavaCapability capability)
    {
        return (s_offeredCapabilities & (1 << (int)capability)) != 0;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError PotentialCapabilities(jvmtiInterface_1_** environment, jvmtiCapabilities* capabilities)
    {
        if (Offered(JavaCapability.RetransformClasses))
            capabilities->can_retransform_classes = 1;

        if (Offered(JavaCapability.RedefineClasses))
            capabilities->can_redefine_classes = 1;

        if (Offered(JavaCapability.GenerateExceptionEvents))
            capabilities->can_generate_exception_events = 1;

        if (Offered(JavaCapability.GenerateMethodEntryEvents))
            capabilities->can_generate_method_entry_events = 1;

        if (Offered(JavaCapability.GenerateMethodExitEvents))
            capabilities->can_generate_method_exit_events = 1;

        if (Offered(JavaCapability.GenerateMonitorEvents))
            capabilities->can_generate_monitor_events = 1;

        if (Offered(JavaCapability.GenerateGarbageCollectionEvents))
            capabilities->can_generate_garbage_collection_events = 1;

        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError ThreadState(jvmtiInterface_1_** environment, _jobject* thread, int* state)
    {
        *state = s_reportedThreadState;

        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError Version(jvmtiInterface_1_** environment, int* version)
    {
        *version = 0x30010000;

        return jvmtiError.JVMTI_ERROR_NONE;
    }

    private sealed class EmptyAgent : JavaAgent;
}

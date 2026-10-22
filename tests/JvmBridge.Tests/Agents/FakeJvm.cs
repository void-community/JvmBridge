using System.Runtime.InteropServices;

using JvmBridge.Agents;
using JvmBridge.Native;

namespace JvmBridge.Tests.Agents;

/// <summary>A JVM that has just enough of a tooling interface to create an <see cref="AgentContext"/>, for tests of agent logic that never calls into the JVM. Use one at a time.</summary>
internal sealed unsafe class FakeJvm : IDisposable
{
    private static nint s_tooling;

    private readonly JNIInvokeInterface_** _machine;
    private readonly jvmtiInterface_1_** _tooling;

    internal FakeJvm()
    {
        jvmtiInterface_1_* toolingTable = (jvmtiInterface_1_*)NativeMemory.AllocZeroed((nuint)sizeof(jvmtiInterface_1_));
        toolingTable->GetVersionNumber = &Version;
        toolingTable->GetPotentialCapabilities = &NoCapabilities;
        toolingTable->SetEventCallbacks = &AcceptCallbacks;
        toolingTable->SetEventNotificationMode = (nint)(delegate* unmanaged[Cdecl]<jvmtiInterface_1_**, jvmtiEventMode, jvmtiEvent, _jobject*, jvmtiError>)&AcceptMode;
        _tooling = (jvmtiInterface_1_**)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        *_tooling = toolingTable;
        JNIInvokeInterface_* machineTable = (JNIInvokeInterface_*)NativeMemory.AllocZeroed((nuint)sizeof(JNIInvokeInterface_));
        machineTable->GetEnv = &GetEnvironment;
        _machine = (JNIInvokeInterface_**)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        *_machine = machineTable;
        s_tooling = (nint)_tooling;
    }

    /// <summary>Gets the pointer that stands for the JVM, as the JVM passes to <c>Agent_OnLoad</c>.</summary>
    internal nint Machine => (nint)_machine;

    public void Dispose()
    {
        AgentRuntime.Stop(Machine);
        s_tooling = 0;
        NativeMemory.Free(*_tooling);
        NativeMemory.Free(_tooling);
        NativeMemory.Free(*_machine);
        NativeMemory.Free(_machine);
    }

    /// <summary>Creates a context that has not enabled events, as one inside Configure.</summary>
    internal AgentContext CreateContext(string name, string options = "")
    {
        return new AgentContext(new EmptyAgent(), (nint)_machine, options, attached: false, name);
    }

    [UnmanagedCallersOnly]
    private static jvmtiError AcceptCallbacks(jvmtiInterface_1_** environment, jvmtiEventCallbacks* callbacks, int size)
    {
        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static jvmtiError AcceptMode(jvmtiInterface_1_** environment, jvmtiEventMode mode, jvmtiEvent notification, _jobject* thread)
    {
        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly]
    private static int GetEnvironment(JNIInvokeInterface_** machine, void** environment, int version)
    {
        *environment = (void*)s_tooling;

        return Methods.JNI_OK;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError NoCapabilities(jvmtiInterface_1_** environment, jvmtiCapabilities* capabilities)
    {
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

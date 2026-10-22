using System.Runtime.InteropServices;

using JvmBridge.Agents;
using JvmBridge.Native;

namespace JvmBridge.Tool.Offline;

/// <summary>
/// A JVM that exists only as two function tables, enough for an agent's <see cref="JavaAgent.Configure"/> to run without a JVM: it offers every capability, accepts every subscription and answers every other call with an error, which
/// the agent sees as an exception it can handle. Its state is static because a JVM calls back through function pointers, so a process hosts one at a time.
/// </summary>
internal sealed unsafe class OfflineJvm : IDisposable
{
    private const int JvmtiVersion = 0x30010000;
    private const int JvmtiVersionMask = 0x70000000;
    private const int JvmtiVersionTag = 0x30000000;

    private static nint s_currentTooling;

    private readonly JNIInvokeInterface_** _machine;
    private readonly jvmtiInterface_1_** _tooling;

    internal OfflineJvm()
    {
        jvmtiInterface_1_* toolingTable = (jvmtiInterface_1_*)NativeMemory.AllocZeroed((nuint)sizeof(jvmtiInterface_1_));

        Fill((nint*)toolingTable, sizeof(jvmtiInterface_1_) / sizeof(nint), (nint)(delegate* unmanaged<jvmtiError>)&Unavailable);

        toolingTable->GetVersionNumber = &Version;
        toolingTable->GetPotentialCapabilities = &PotentialCapabilities;
        toolingTable->AddCapabilities = &Accept;
        toolingTable->SetEventCallbacks = &AcceptCallbacks;
        toolingTable->DisposeEnvironment = &Dispose;
        _tooling = (jvmtiInterface_1_**)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        *_tooling = toolingTable;

        JNIInvokeInterface_* machineTable = (JNIInvokeInterface_*)NativeMemory.AllocZeroed((nuint)sizeof(JNIInvokeInterface_));

        Fill((nint*)machineTable, sizeof(JNIInvokeInterface_) / sizeof(nint), (nint)(delegate* unmanaged<int>)&Failed);

        machineTable->GetEnv = &GetEnvironment;
        _machine = (JNIInvokeInterface_**)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        *_machine = machineTable;
        s_currentTooling = (nint)_tooling;
    }

    /// <summary>Gets the pointer that stands for the JVM.</summary>
    internal nint Machine => (nint)_machine;

    /// <inheritdoc/>
    public void Dispose()
    {
        s_currentTooling = 0;
        NativeMemory.Free(*_tooling);
        NativeMemory.Free(_tooling);
        NativeMemory.Free(*_machine);
        NativeMemory.Free(_machine);
    }

    [UnmanagedCallersOnly]
    private static jvmtiError Accept(jvmtiInterface_1_** environment, jvmtiCapabilities* capabilities)
    {
        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError AcceptCallbacks(jvmtiInterface_1_** environment, jvmtiEventCallbacks* callbacks, int size)
    {
        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError Dispose(jvmtiInterface_1_** environment)
    {
        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly]
    private static int Failed()
    {
        return Methods.JNI_ERR;
    }

    private static void Fill(nint* slots, int count, nint value)
    {
        for (int index = 0; index < count; index++)
            slots[index] = value;
    }

    [UnmanagedCallersOnly]
    private static int GetEnvironment(JNIInvokeInterface_** machine, void** environment, int version)
    {
        if ((version & JvmtiVersionMask) != JvmtiVersionTag)
            return Methods.JNI_EVERSION;

        *environment = (void*)s_currentTooling;

        return Methods.JNI_OK;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError PotentialCapabilities(jvmtiInterface_1_** environment, jvmtiCapabilities* capabilities)
    {
        capabilities->can_retransform_classes = 1;
        capabilities->can_redefine_classes = 1;
        capabilities->can_generate_exception_events = 1;
        capabilities->can_generate_method_entry_events = 1;
        capabilities->can_generate_method_exit_events = 1;
        capabilities->can_generate_monitor_events = 1;
        capabilities->can_generate_garbage_collection_events = 1;

        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError Unavailable()
    {
        return jvmtiError.JVMTI_ERROR_NOT_AVAILABLE;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError Version(jvmtiInterface_1_** environment, int* version)
    {
        *version = JvmtiVersion;

        return jvmtiError.JVMTI_ERROR_NONE;
    }
}

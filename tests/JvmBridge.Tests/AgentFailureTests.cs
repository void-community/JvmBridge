using System.Runtime.InteropServices;

using JvmBridge.Agents;
using JvmBridge.Native;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>Verifies startup failures remain inside the managed native-entry boundary.</summary>
public sealed unsafe class AgentFailureTests
{
    [ThreadStatic]
    private static int s_disposeCount;
    [ThreadStatic]
    private static nint s_tooling;

    /// <summary>A user factory exception becomes JNI_ERR rather than crossing the native boundary.</summary>
    [Fact]
    public void FactoryFailureIsContained()
    {
        int result = AgentRuntime.Start(static () => throw new InvalidOperationException(message: "Factory failure"), machine: 1, options: 0, attached: false);
        Assert.Equal(Methods.JNI_ERR, result);
    }

    /// <summary>Failure after acquiring JVMTI releases the environment before returning JNI_ERR.</summary>
    [Fact]
    public void FailedVersionQueryDisposesAcquiredEnvironment()
    {
        s_disposeCount = 0;
        jvmtiInterface_1_ toolingTable = new() { GetVersionNumber = &FailVersion, DisposeEnvironment = &DisposeEnvironment };
        jvmtiInterface_1_* toolingPointer = &toolingTable;
        s_tooling = (nint)(&toolingPointer);
        JNIInvokeInterface_ machineTable = new() { GetEnv = &GetEnvironment };
        JNIInvokeInterface_* machinePointer = &machineTable;

        try
        {
            int result = AgentRuntime.Start(static () => new EmptyAgent(), (nint)(&machinePointer), options: 0, attached: false);
            Assert.Equal(Methods.JNI_ERR, result);
            Assert.Equal(expected: 1, s_disposeCount);
        }
        finally { s_tooling = 0; }
    }

    [UnmanagedCallersOnly]
    private static jvmtiError DisposeEnvironment(jvmtiInterface_1_** environment)
    {
        s_disposeCount++;

        return jvmtiError.JVMTI_ERROR_NONE;
    }

    [UnmanagedCallersOnly]
    private static jvmtiError FailVersion(jvmtiInterface_1_** environment, int* version)
    {
        return jvmtiError.JVMTI_ERROR_INTERNAL;
    }

    [UnmanagedCallersOnly]
    private static int GetEnvironment(JNIInvokeInterface_** machine, void** environment, int version)
    {
        *environment = (void*)s_tooling;

        return Methods.JNI_OK;
    }

    private sealed class EmptyAgent : JavaAgent;
}

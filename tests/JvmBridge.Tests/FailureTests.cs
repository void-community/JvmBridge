using System.Runtime.InteropServices;

using JvmBridge.Native;
using JvmBridge.Runtime;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>
/// Verifies that managed guards preserve JNI ownership when native operations fail.
/// </summary>
public sealed unsafe class FailureTests
{
    /// <summary>Verifies JVM lookup fails before JNI can allocate a global reference.</summary>
    [Fact]
    public void FailedMachineLookupDoesNotAllocateGlobalReference()
    {
        // NewGlobalRef deliberately has no function pointer: reaching it would crash the test process.
        JNINativeInterface_ table = new() { NewLocalRef = &Duplicate, GetJavaVM = &FailMachine, ExceptionCheck = &NoException, DeleteLocalRef = &Delete };
        JNINativeInterface_* pointer = &table;

        using JavaEnvironment environment = new((nint)(&pointer));

        using JavaLocalReference local = environment.NewLocalReference(value: 123);

        JniException failure = Assert.Throws<JniException>(local.ToGlobal);
        Assert.Equal(Methods.JNI_ERR, failure.ErrorCode);
        Assert.Equal(expected: 123, local.Handle);
    }

    /// <summary>Verifies JVM lookup fails before JNI can allocate a weak global reference.</summary>
    [Fact]
    public void FailedMachineLookupDoesNotAllocateWeakReference()
    {
        JNINativeInterface_ table = new() { GetJavaVM = &FailMachine };
        JNINativeInterface_* pointer = &table;

        using JavaEnvironment environment = new((nint)(&pointer));

        JniException failure = Assert.Throws<JniException>(() => environment.NewWeakGlobalReference(value: 123));
        Assert.Equal(Methods.JNI_ERR, failure.ErrorCode);
    }

    /// <summary>
    /// Verifies that a failed global-reference promotion leaves the local reference usable.
    /// </summary>
    [Fact]
    public void FailedPromotionPreservesTheLocalReference()
    {
        JNINativeInterface_ table = new() { NewLocalRef = &Duplicate, NewGlobalRef = &FailPromotion, GetJavaVM = &GetMachine, ExceptionCheck = &NoException, DeleteLocalRef = &Delete };
        JNINativeInterface_* pointer = &table;

        using JavaEnvironment environment = new((nint)(&pointer));

        using JavaLocalReference local = environment.NewLocalReference(value: 123);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(local.ToGlobal);
        Assert.Contains(expectedSubstring: "NewGlobalRef", exception.Message, StringComparison.Ordinal);
        Assert.Equal(expected: 123, local.Handle);
    }

    /// <summary>Verifies that duplicating Java null returns an empty disposable local reference.</summary>
    [Fact]
    public void NullLocalReferenceRemainsEmpty()
    {
        JNINativeInterface_ table = new() { NewLocalRef = &Duplicate, ExceptionCheck = &NoException, DeleteLocalRef = &Delete };
        JNINativeInterface_* pointer = &table;

        using JavaEnvironment environment = new((nint)(&pointer));

        using JavaLocalReference empty = environment.NewLocalReference(value: 0);

        Assert.Equal(expected: 0, empty.Handle);
    }

    /// <summary>
    /// Verifies that null JNI method identifiers are rejected before a function-table call.
    /// </summary>
    [Fact]
    public void NullMethodsAreRejectedBeforeNativeInvocation()
    {
        using JavaEnvironment environment = new(environment: 1);

        void Invocation()
        {
            int unexpectedResult = environment.CallInt(receiver: 123, method: 0, []);

            throw new InvalidOperationException($"Expected a null method identifier to fail, but JNI returned {unexpectedResult}.");
        }

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(Invocation);

        Assert.Equal(expected: "method", exception.ParamName);
    }

    [UnmanagedCallersOnly]
    private static void Delete(JNINativeInterface_** environment, _jobject* value) { }

    [UnmanagedCallersOnly]
    private static _jobject* Duplicate(JNINativeInterface_** environment, _jobject* value)
    {
        return value;
    }

    [UnmanagedCallersOnly]
    private static int FailMachine(JNINativeInterface_** environment, JNIInvokeInterface_*** machine)
    {
        return Methods.JNI_ERR;
    }

    [UnmanagedCallersOnly]
    private static _jobject* FailPromotion(JNINativeInterface_** environment, _jobject* value)
    {
        return null;
    }

    [UnmanagedCallersOnly]
    private static int GetMachine(JNINativeInterface_** environment, JNIInvokeInterface_*** machine)
    {
        *machine = (JNIInvokeInterface_**)1;

        return Methods.JNI_OK;
    }

    [UnmanagedCallersOnly]
    private static byte NoException(JNINativeInterface_** environment)
    {
        return 0;
    }
}

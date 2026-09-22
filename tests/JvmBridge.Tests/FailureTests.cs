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
    /// <summary>
    /// Verifies that a failed global-reference promotion leaves the local reference usable.
    /// </summary>
    [Fact]
    public void FailedPromotionPreservesTheLocalReference()
    {
        JNINativeInterface_ table = new() { NewLocalRef = &Duplicate, NewGlobalRef = &FailPromotion, ExceptionCheck = &NoException, DeleteLocalRef = &Delete };
        JNINativeInterface_* pointer = &table;

        using JavaEnvironment environment = new((nint)(&pointer));

        using JavaLocalReference local = environment.NewLocalReference(value: 123);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(local.ToGlobal);
        Assert.Contains(expectedSubstring: "NewGlobalRef", exception.Message, StringComparison.Ordinal);
        Assert.Equal(expected: 123, local.Handle);
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
    private static _jobject* FailPromotion(JNINativeInterface_** environment, _jobject* value)
    {
        return null;
    }

    [UnmanagedCallersOnly]
    private static byte NoException(JNINativeInterface_** environment)
    {
        return 0;
    }
}

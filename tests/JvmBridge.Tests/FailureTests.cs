using System.Runtime.InteropServices;
using JvmBridge.Native;
using JvmBridge.Runtime;
using Xunit;

namespace JvmBridge.Tests;

public sealed unsafe class FailureTests
{
    [UnmanagedCallersOnly]
    private static _jobject* Duplicate(JNINativeInterface_** environment, _jobject* value) => value;
    [UnmanagedCallersOnly]
    private static _jobject* FailPromotion(JNINativeInterface_** environment, _jobject* value) => null;
    [UnmanagedCallersOnly]
    private static byte NoException(JNINativeInterface_** environment) => 0;
    [UnmanagedCallersOnly]
    private static void Delete(JNINativeInterface_** environment, _jobject* value) { }

    [Fact]
    public void FailedPromotionPreservesTheLocalReference()
    {
        JNINativeInterface_ table = new() { NewLocalRef = &Duplicate, NewGlobalRef = &FailPromotion, ExceptionCheck = &NoException, DeleteLocalRef = &Delete };
        JNINativeInterface_* pointer = &table;
        using JavaEnvironment environment = new((nint)(&pointer));
        using JavaLocalReference local = environment.NewLocalReference(123);
        Assert.Throws<InvalidOperationException>(() => local.ToGlobal());
        Assert.Equal((nint)123, local.Handle);
    }

    [Fact]
    public void NullMethodsAreRejectedBeforeNativeInvocation()
    {
        using JavaEnvironment environment = new(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => environment.CallInt(123, 0, []));
    }
}

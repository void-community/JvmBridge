using System.Runtime.InteropServices;

using JvmBridge.Native;
using JvmBridge.Runtime;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>Verifies attachment scopes own only the thread attachments they create.</summary>
public sealed unsafe class ThreadAttachmentTests
{
    [ThreadStatic]
    private static int s_detachCount;

    /// <summary>An existing attachment belongs to the caller, not the borrowed scope.</summary>
    [Fact]
    public void BorrowedThreadIsNotDetached()
    {
        s_detachCount = 0;
        JNIInvokeInterface_ table = new() { GetEnv = &AlreadyAttached, DetachCurrentThread = &Detach };
        JNIInvokeInterface_* pointer = &table;

        using JavaVirtualMachine machine = JavaVirtualMachine.Borrow((nint)(&pointer));

        JavaThreadAttachment attachment = machine.AttachCurrentThread();
        attachment.Dispose();
        Assert.Equal(expected: 0, s_detachCount);
    }

    /// <summary>Disposing a borrowed environment early must not suppress scope detachment.</summary>
    [Fact]
    public void DisposedEnvironmentStillAllowsOwnedThreadDetachment()
    {
        s_detachCount = 0;
        JNIInvokeInterface_ table = new() { GetEnv = &Detached, AttachCurrentThreadAsDaemon = &Attach, DetachCurrentThread = &Detach };
        JNIInvokeInterface_* pointer = &table;

        using JavaVirtualMachine machine = JavaVirtualMachine.Borrow((nint)(&pointer));

        JavaThreadAttachment attachment = machine.AttachCurrentThread();
        attachment.Environment.Dispose();
        attachment.Dispose();
        attachment.Dispose();
        Assert.Equal(expected: 1, s_detachCount);
    }

    [UnmanagedCallersOnly]
    private static int AlreadyAttached(JNIInvokeInterface_** machine, void** environment, int version)
    {
        *environment = (void*)1;

        return Methods.JNI_OK;
    }

    [UnmanagedCallersOnly]
    private static int Attach(JNIInvokeInterface_** machine, void** environment, void* arguments)
    {
        *environment = (void*)1;

        return Methods.JNI_OK;
    }

    [UnmanagedCallersOnly]
    private static int Detach(JNIInvokeInterface_** machine)
    {
        s_detachCount++;

        return Methods.JNI_OK;
    }

    [UnmanagedCallersOnly]
    private static int Detached(JNIInvokeInterface_** machine, void** environment, int version)
    {
        return Methods.JNI_EDETACHED;
    }
}

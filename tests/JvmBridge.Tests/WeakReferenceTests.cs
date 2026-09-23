using System.Runtime.InteropServices;

using JvmBridge.Native;
using JvmBridge.Runtime;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>Verifies weak JNI ownership and checked identity with simulated JNI tables.</summary>
public sealed unsafe class WeakReferenceTests
{
    [ThreadStatic] private static int s_clearedExceptions;
    [ThreadStatic] private static bool s_collected;
    [ThreadStatic] private static int s_deletedLocal;
    [ThreadStatic] private static int s_deletedWeak;
    [ThreadStatic] private static int s_detachCount;
    [ThreadStatic] private static bool s_detached;
    [ThreadStatic] private static nint s_environment;
    [ThreadStatic] private static bool s_failCreation;
    [ThreadStatic] private static nint s_machine;
    [ThreadStatic] private static bool s_pendingException;

    /// <summary>A missing referent does not produce a local owner, unlike a pending exception.</summary>
    [Fact]
    public void CollectedReferentAndJavaExceptionAreDistinct()
    {
        JNINativeInterface_ native = NativeTable();
        JNINativeInterface_* nativePointer = &native;
        JNIInvokeInterface_ invocation = InvocationTable();
        JNIInvokeInterface_* invocationPointer = &invocation;
        Initialize((nint)(&nativePointer), (nint)(&invocationPointer));

        using JavaEnvironment environment = new(s_environment);

        using JavaWeakGlobalReference weak = environment.NewWeakGlobalReference(value: 123);

        s_collected = true;
        Assert.Null(weak.TryPromote(environment));
        s_pendingException = true;
        JavaException failure = Assert.Throws<JavaException>(() => weak.TryPromote(environment));
        Assert.Equal(expected: "TryPromoteWeak", failure.Operation);
        Assert.Equal(expected: 1, s_clearedExceptions);
        Assert.Null(weak.TryPromote(environment));
    }

    /// <summary>A JNI allocation failure cannot create an owned weak reference.</summary>
    [Fact]
    public void CreationChecksFailuresAndRejectsNull()
    {
        JNINativeInterface_ native = NativeTable();
        JNINativeInterface_* nativePointer = &native;
        JNIInvokeInterface_ invocation = InvocationTable();
        JNIInvokeInterface_* invocationPointer = &invocation;
        Initialize((nint)(&nativePointer), (nint)(&invocationPointer));

        using JavaEnvironment environment = new(s_environment);

        ArgumentOutOfRangeException invalid = Assert.Throws<ArgumentOutOfRangeException>(() => environment.NewWeakGlobalReference(value: 0));
        Assert.Equal(expected: "value", invalid.ParamName);
        s_failCreation = true;
        Assert.Contains(
            expectedSubstring: "NewWeakGlobalRef",
            Assert.Throws<InvalidOperationException>(() => environment.NewWeakGlobalReference(value: 123)).Message,
            StringComparison.Ordinal
        );
        s_pendingException = true;
        JavaException failure = Assert.Throws<JavaException>(() => environment.NewWeakGlobalReference(value: 123));
        Assert.Equal(nameof(JavaEnvironment.NewWeakGlobalReference), failure.Operation);
        Assert.Equal(expected: 1, s_clearedExceptions);
    }

    /// <summary>Identity checks report and clear Java exceptions rather than returning a boolean.</summary>
    [Fact]
    public void IdentityCheckReportsJavaExceptions()
    {
        JNINativeInterface_ native = NativeTable();
        JNINativeInterface_* nativePointer = &native;
        JNIInvokeInterface_ invocation = InvocationTable();
        JNIInvokeInterface_* invocationPointer = &invocation;
        Initialize((nint)(&nativePointer), (nint)(&invocationPointer));

        using JavaEnvironment environment = new(s_environment);

        s_pendingException = true;
        void Compare()
        {
            bool same = environment.IsSameObject(first: 123, second: 123);
            Assert.True(same);
        }

        JavaException failure = Assert.Throws<JavaException>(Compare);
        Assert.Equal(nameof(JavaEnvironment.IsSameObject), failure.Operation);
        Assert.Equal(expected: 1, s_clearedExceptions);
    }

    /// <summary>Creates a weak reference and promotes it without transferring local ownership.</summary>
    [Fact]
    public void PromotionOwnsLocalAndDisposalDeletesWeakOnce()
    {
        JNINativeInterface_ native = NativeTable();
        JNINativeInterface_* nativePointer = &native;
        JNIInvokeInterface_ invocation = InvocationTable();
        JNIInvokeInterface_* invocationPointer = &invocation;
        Initialize((nint)(&nativePointer), (nint)(&invocationPointer));

        using JavaEnvironment environment = new(s_environment);

        JavaWeakGlobalReference weak = environment.NewWeakGlobalReference(value: 123);
        Assert.Equal(expected: 456, weak.Handle);

        using (JavaLocalReference? local = weak.TryPromote(environment))
        {
            Assert.NotNull(local);
            Assert.Equal(expected: 789, local.Handle);
            Assert.True(environment.IsSameObject(local.Handle, second: 123));
            Assert.False(environment.IsSameObject(local.Handle, second: 999));
            Assert.True(environment.IsSameObject(first: 0, second: 0));
            Assert.False(environment.IsSameObject(first: 0, local.Handle));
        }

        Assert.Equal(expected: 1, s_deletedLocal);

        s_detached = true;
        weak.Dispose();
        weak.Dispose();
        Assert.Equal(expected: 1, s_deletedWeak);
        Assert.Equal(expected: 1, s_detachCount);
        ObjectDisposedException disposedPromotion = Assert.Throws<ObjectDisposedException>(() => weak.TryPromote(environment));
        Assert.Equal(typeof(JavaWeakGlobalReference).FullName, disposedPromotion.ObjectName);
        ObjectDisposedException disposedHandle = Assert.Throws<ObjectDisposedException>(() => { nint handle = weak.Handle; Assert.Equal(expected: 456, handle); });
        Assert.Equal(nameof(JavaWeakGlobalReference), disposedHandle.ObjectName);
    }

    /// <summary>Promotion rejects the wrong machine, thread, or environment scope.</summary>
    [Fact]
    public void PromotionRejectsInaccessibleEnvironment()
    {
        JNINativeInterface_ native = NativeTable();
        JNINativeInterface_* nativePointer = &native;
        JNIInvokeInterface_ invocation = InvocationTable();
        JNIInvokeInterface_* invocationPointer = &invocation;
        Initialize((nint)(&nativePointer), (nint)(&invocationPointer));

        using JavaEnvironment environment = new(s_environment);

        using JavaWeakGlobalReference weak = environment.NewWeakGlobalReference(value: 123);

        s_machine = 1;
        InvalidOperationException wrongMachine = Assert.Throws<InvalidOperationException>(() => weak.TryPromote(environment));
        Assert.Contains(expectedSubstring: "different JVM", wrongMachine.Message, StringComparison.Ordinal);
        s_machine = (nint)(&invocationPointer);

        Exception? wrongThread = null;
        Thread worker = new(() => wrongThread = Record.Exception(() => weak.TryPromote(environment)));
        worker.Start();
        worker.Join();
        InvalidOperationException threadException = Assert.IsType<InvalidOperationException>(wrongThread);
        Assert.Contains(expectedSubstring: "threads", threadException.Message, StringComparison.Ordinal);

        environment.Dispose();
        ObjectDisposedException disposedEnvironment = Assert.Throws<ObjectDisposedException>(() => weak.TryPromote(environment));
        Assert.Equal(typeof(JavaEnvironment).FullName, disposedEnvironment.ObjectName);
    }

    [UnmanagedCallersOnly]
    private static int Attach(JNIInvokeInterface_** machine, void** environment, void* arguments)
    {
        *environment = (void*)s_environment;

        return Methods.JNI_OK;
    }

    [UnmanagedCallersOnly]
    private static void DeleteLocal(JNINativeInterface_** environment, _jobject* value)
    {
        s_deletedLocal++;
    }

    [UnmanagedCallersOnly]
    private static void DeleteWeak(JNINativeInterface_** environment, _jobject* value)
    {
        s_deletedWeak++;
    }

    [UnmanagedCallersOnly]
    private static int Detach(JNIInvokeInterface_** machine)
    {
        s_detachCount++;

        return Methods.JNI_OK;
    }

    [UnmanagedCallersOnly]
    private static byte ExceptionCheck(JNINativeInterface_** environment)
    {
        return s_pendingException ? (byte)1 : (byte)0;
    }

    [UnmanagedCallersOnly]
    private static void ExceptionClear(JNINativeInterface_** environment)
    {
        s_clearedExceptions++;
        s_pendingException = false;
    }

    [UnmanagedCallersOnly]
    private static int GetEnvironment(JNIInvokeInterface_** machine, void** environment, int version)
    {
        if (s_detached)
            return Methods.JNI_EDETACHED;

        *environment = (void*)s_environment;

        return Methods.JNI_OK;
    }

    [UnmanagedCallersOnly]
    private static int GetMachine(JNINativeInterface_** environment, JNIInvokeInterface_*** machine)
    {
        *machine = (JNIInvokeInterface_**)s_machine;

        return Methods.JNI_OK;
    }

    private static void Initialize(nint environment, nint machine)
    {
        s_environment = environment;
        s_machine = machine;
        s_collected = false;
        s_pendingException = false;
        s_failCreation = false;
        s_detached = false;
        s_deletedWeak = 0;
        s_deletedLocal = 0;
        s_clearedExceptions = 0;
        s_detachCount = 0;
    }

    private static JNIInvokeInterface_ InvocationTable()
    {
        return new()
        {
            GetEnv = &GetEnvironment,
            AttachCurrentThreadAsDaemon = &Attach,
            DetachCurrentThread = &Detach
        };
    }

    private static JNINativeInterface_ NativeTable()
    {
        return new()
        {
            GetJavaVM = &GetMachine,
            NewWeakGlobalRef = &NewWeak,
            DeleteWeakGlobalRef = &DeleteWeak,
            NewLocalRef = &NewLocal,
            DeleteLocalRef = &DeleteLocal,
            IsSameObject = &SameObject,
            ExceptionCheck = &ExceptionCheck,
            ExceptionClear = &ExceptionClear
        };
    }

    [UnmanagedCallersOnly]
    private static _jobject* NewLocal(JNINativeInterface_** environment, _jobject* value)
    {
        return s_collected ? null : (_jobject*)789;
    }

    [UnmanagedCallersOnly]
    private static _jobject* NewWeak(JNINativeInterface_** environment, _jobject* value)
    {
        return s_failCreation ? null : (_jobject*)456;
    }

    [UnmanagedCallersOnly]
    private static byte SameObject(JNINativeInterface_** environment, _jobject* first, _jobject* second)
    {
        return first == null || second == null
            ? first == second ? (byte)1 : (byte)0
            : first == (_jobject*)999 || second == (_jobject*)999 ? (byte)0 : (byte)1;
    }
}

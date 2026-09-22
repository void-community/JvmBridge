using System.Runtime.InteropServices;

using JvmBridge.Agents;
using JvmBridge.Native;
using JvmBridge.Runtime;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>
/// Verifies managed runtime ownership, ABI layout, and modified UTF-8 behavior.
/// </summary>
public sealed unsafe class RuntimeTests
{
    private static int s_clearedExceptions;
    private static byte[]? s_definedBytes;
    private static nint s_definedLoader;
    private static byte[]? s_definedName;
    private static nint s_deletedReference;
    private static bool s_pendingDefinitionException;

    /// <summary>Checks that a failed definition clears Java's exception before raising the managed error.</summary>
    [Fact]
    public void DefineClassClearsPendingJavaException()
    {
        JNINativeInterface_ table = new() { DefineClass = &FakeDefineClass, ExceptionCheck = &FakeExceptionCheck, ExceptionClear = &FakeExceptionClear };
        JNINativeInterface_* functions = &table;

        nint environmentHandle = (nint)(&functions);

        using JavaEnvironment environment = new(environmentHandle);

        s_pendingDefinitionException = true;
        s_clearedExceptions = 0;

        JavaException exception = Assert.Throws<JavaException>(() => environment.DefineClass(name: "example/Agent", loader: 0, [1]));

        Assert.Equal(nameof(JavaEnvironment.DefineClass), exception.Operation);
        Assert.Equal(expected: 1, s_clearedExceptions);
        Assert.False(s_pendingDefinitionException);
        Assert.Equal(expected: 0, s_definedLoader);
    }

    /// <summary>Checks class-file bytes, loader identity, local-reference ownership, and modified UTF-8 names.</summary>
    [Fact]
    public void DefineClassOwnsTheReturnedLocalReference()
    {
        JNINativeInterface_ table = new() { DefineClass = &FakeDefineClass, ExceptionCheck = &FakeExceptionCheck, ExceptionClear = &FakeExceptionClear, DeleteLocalRef = &FakeDeleteLocal };
        JNINativeInterface_* functions = &table;

        nint environmentHandle = (nint)(&functions);

        using JavaEnvironment environment = new(environmentHandle);

        s_pendingDefinitionException = false;
        s_deletedReference = 0;

        using (JavaLocalReference type = environment.DefineClass(name: "example/Agent", loader: 123, [0xca, 0xfe, 0xba, 0xbe]))
            Assert.Equal(expected: 456, type.Handle);

        Assert.Equal(ModifiedUtf8.Encode(value: "example/Agent"), s_definedName);
        Assert.Equal(new byte[] { 0xca, 0xfe, 0xba, 0xbe }, s_definedBytes);
        Assert.Equal(expected: 123, s_definedLoader);
        Assert.Equal(expected: 456, s_deletedReference);
    }

    /// <summary>Verifies that optional callback references and the JNI environment preserve their absent values.</summary>
    [Fact]
    public void EmptyTransformContextHasNoBorrowedReferences()
    {
        ClassTransformContext callback = default;

        Assert.Null(callback.Environment);
        Assert.Equal(expected: 0, callback.Loader);
        Assert.Equal(expected: 0, callback.ProtectionDomain);
        Assert.Equal(expected: 0, callback.ClassBeingRedefined);
        Assert.False(callback.CanUseJni);
    }

    /// <summary>
    /// Verifies that a JNI environment rejects access from a different managed thread.
    /// </summary>
    [Fact]
    public void EnvironmentRejectsCrossThreadAccessBeforeCallingNativeCode()
    {
        using JavaEnvironment environment = new(environment: 1);

        InvalidOperationException? observedException = null;
        nint? unexpectedHandle = null;

        Thread worker = new(
            () =>
        {
            try { unexpectedHandle = environment.Handle; }
            catch (InvalidOperationException exception) { observedException = exception; }
        }
        );

        worker.Start();
        worker.Join();
        Assert.False(unexpectedHandle.HasValue);
        Assert.NotNull(observedException);
    }

    /// <summary>
    /// Verifies that a disposed JNI environment rejects subsequent access.
    /// </summary>
    [Fact]
    public void EnvironmentRejectsDisposedAccess()
    {
        JavaEnvironment environment = new(environment: 1);
        environment.Dispose();

        void AccessEnvironment()
        {
            nint unexpectedHandle = environment.Handle;

            throw new InvalidOperationException($"Expected disposed access to fail, but received handle {unexpectedHandle}.");
        }

        ObjectDisposedException exception = Assert.Throws<ObjectDisposedException>(AccessEnvironment);

        Assert.NotEmpty(exception.Message);
    }

    /// <summary>
    /// Verifies that generated heap callback signatures use pointers for reference metadata.
    /// </summary>
    [Fact]
    public void HeapCallbacksTakePointersToReferenceInformation()
    {
        jvmtiHeapCallbacks callbacks = default;
        delegate* unmanaged<jvmtiHeapReferenceKind, jvmtiHeapReferenceInfo*, long, long, long, long*, long*, int, void*, int> reference = callbacks.heap_reference_callback;
        delegate* unmanaged<jvmtiHeapReferenceKind, jvmtiHeapReferenceInfo*, long, long*, jvalue, jvmtiPrimitiveType, void*, int> primitive = callbacks.primitive_field_callback;
        Assert.True(reference == null && primitive == null);
    }

    /// <summary>
    /// Verifies stable JNI function-table slots and native value layouts.
    /// </summary>
    [Fact]
    public void JavaInterfacesPreserveEstablishedSlots()
    {
        Assert.Equal(171 * IntPtr.Size, Marshal.OffsetOf<JNINativeInterface_>(nameof(JNINativeInterface_.GetArrayLength)).ToInt32());
        Assert.Equal(219 * IntPtr.Size, Marshal.OffsetOf<JNINativeInterface_>(nameof(JNINativeInterface_.GetJavaVM)).ToInt32());
        Assert.Equal(235 * IntPtr.Size, Marshal.OffsetOf<JNINativeInterface_>(nameof(JNINativeInterface_.GetStringUTFLengthAsLong)).ToInt32());
        Assert.Equal(expected: 16, sizeof(jvmtiCapabilities));
        Assert.Equal(expected: 8, sizeof(jvalue));
    }

    /// <summary>
    /// Verifies that modified UTF-8 round-trips unpaired UTF-16 surrogate code units.
    /// </summary>
    [Fact]
    public void ModifiedUtf8PreservesUnpairedSurrogates()
    {
        string value = new(['\ud800', 'x', '\udfff']);
        byte[] encoded = ModifiedUtf8.Encode(value);
        Assert.Equal(value, ModifiedUtf8.Decode(encoded.AsSpan(start: 0, encoded.Length - 1)));
    }

    /// <summary>
    /// Verifies that modified UTF-8 round-trips representative UTF-16 values.
    /// </summary>
    /// <param name="value">The value to encode and decode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("ASCII")]
    [InlineData("A\0B")]
    [InlineData("\uD83D\uDE00")]
    public void ModifiedUtf8PreservesUtf16CodeUnits(string value)
    {
        byte[] encoded = ModifiedUtf8.Encode(value);
        Assert.Equal(expected: 0, encoded[^1]);
        Assert.DoesNotContain((byte)0, encoded[..^1]);
        Assert.Equal(value, ModifiedUtf8.Decode(encoded.AsSpan(start: 0, encoded.Length - 1)));
    }

    /// <summary>
    /// Verifies that standard four-byte UTF-8 sequences are rejected as invalid modified UTF-8.
    /// </summary>
    [Fact]
    public void ModifiedUtf8RejectsStandardFourByteEncoding()
    {
        FormatException exception = Assert.Throws<FormatException>(static () => { string unexpectedValue = ModifiedUtf8.Decode([0xf0, 0x9f, 0x98, 0x80]); GC.KeepAlive(unexpectedValue); });

        Assert.NotEmpty(exception.Message);
    }

    [UnmanagedCallersOnly]
    private static _jobject* FakeDefineClass(JNINativeInterface_** environment, byte* name, _jobject* loader, sbyte* bytes, int length)
    {
        int nameLength = 0;

        while (name[nameLength] != 0)
            nameLength++;

        s_definedName = new ReadOnlySpan<byte>(name, nameLength + 1).ToArray();
        s_definedBytes = new ReadOnlySpan<byte>(bytes, length).ToArray();
        s_definedLoader = (nint)loader;

        return s_pendingDefinitionException ? null : (_jobject*)456;
    }

    [UnmanagedCallersOnly]
    private static void FakeDeleteLocal(JNINativeInterface_** environment, _jobject* value)
    {
        s_deletedReference = (nint)value;
    }

    [UnmanagedCallersOnly]
    private static byte FakeExceptionCheck(JNINativeInterface_** environment)
    {
        return s_pendingDefinitionException ? (byte)1 : (byte)0;
    }

    [UnmanagedCallersOnly]
    private static void FakeExceptionClear(JNINativeInterface_** environment)
    {
        s_clearedExceptions++;
        s_pendingDefinitionException = false;
    }
}

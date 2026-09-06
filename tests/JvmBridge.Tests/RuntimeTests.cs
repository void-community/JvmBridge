using System.Runtime.InteropServices;
using JvmBridge.Native;
using JvmBridge.Runtime;
using Xunit;

namespace JvmBridge.Tests;

public sealed unsafe class RuntimeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("ASCII")]
    [InlineData("A\0B")]
    [InlineData("\uD83D\uDE00")]
    public void ModifiedUtf8PreservesUtf16CodeUnits(string value)
    {
        byte[] encoded = ModifiedUtf8.Encode(value);
        Assert.Equal(0, encoded[^1]);
        Assert.DoesNotContain((byte)0, encoded[..^1]);
        Assert.Equal(value, ModifiedUtf8.Decode(encoded.AsSpan(0, encoded.Length - 1)));
    }

    [Fact]
    public void ModifiedUtf8PreservesUnpairedSurrogates()
    {
        string value = new(['\ud800', 'x', '\udfff']);
        byte[] encoded = ModifiedUtf8.Encode(value);
        Assert.Equal(value, ModifiedUtf8.Decode(encoded.AsSpan(0, encoded.Length - 1)));
    }

    [Fact]
    public void ModifiedUtf8RejectsStandardFourByteEncoding() => Assert.Throws<FormatException>(() => ModifiedUtf8.Decode([0xf0, 0x9f, 0x98, 0x80]));

    [Fact]
    public void JavaInterfacesPreserveEstablishedSlots()
    {
        Assert.Equal(171 * IntPtr.Size, Marshal.OffsetOf<JNINativeInterface_>(nameof(JNINativeInterface_.GetArrayLength)).ToInt32());
        Assert.Equal(219 * IntPtr.Size, Marshal.OffsetOf<JNINativeInterface_>(nameof(JNINativeInterface_.GetJavaVM)).ToInt32());
        Assert.Equal(235 * IntPtr.Size, Marshal.OffsetOf<JNINativeInterface_>(nameof(JNINativeInterface_.GetStringUTFLengthAsLong)).ToInt32());
        Assert.Equal(16, sizeof(jvmtiCapabilities));
        Assert.Equal(8, sizeof(jvalue));
    }

    [Fact]
    public void HeapCallbacksTakePointersToReferenceInformation()
    {
        jvmtiHeapCallbacks callbacks = default;
        delegate* unmanaged<jvmtiHeapReferenceKind, jvmtiHeapReferenceInfo*, long, long, long, long*, long*, int, void*, int> reference = callbacks.heap_reference_callback;
        delegate* unmanaged<jvmtiHeapReferenceKind, jvmtiHeapReferenceInfo*, long, long*, jvalue, jvmtiPrimitiveType, void*, int> primitive = callbacks.primitive_field_callback;
        Assert.True(reference == null && primitive == null);
    }

    [Fact]
    public void EnvironmentRejectsCrossThreadAccessBeforeCallingNativeCode()
    {
        using JavaEnvironment environment = new(1);
        Exception? observed = null;
        Thread worker = new(() => { try { _ = environment.Handle; } catch (Exception exception) { observed = exception; } });
        worker.Start();
        worker.Join();
        Assert.IsType<InvalidOperationException>(observed);
    }

    [Fact]
    public void EnvironmentRejectsDisposedAccess()
    {
        JavaEnvironment environment = new(1);
        environment.Dispose();
        Assert.Throws<ObjectDisposedException>(() => environment.Handle);
    }
}

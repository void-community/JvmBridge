namespace JvmBridge.Runtime;

/// <summary>An owned global reference, usable from attached threads while its JVM is alive.</summary>
public sealed class JavaGlobalReference : IDisposable
{
    private readonly JavaVirtualMachine _machine;
    private nint _handle;
    internal JavaGlobalReference(JavaVirtualMachine machine, nint handle) { _machine = machine; _handle = handle; }

    /// <summary>Gets the raw global-reference handle while this reference remains owned.</summary>
    public nint Handle => _handle != 0 ? _handle : throw new ObjectDisposedException(nameof(JavaGlobalReference));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_handle == 0)
            return;
        using JavaThreadAttachment attachment = _machine.AttachCurrentThread();
        nint handle = Interlocked.Exchange(ref _handle, 0);
        if (handle == 0)
            return;
        attachment.Environment.DeleteGlobal(handle);
    }
}

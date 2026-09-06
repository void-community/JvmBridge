namespace JvmBridge.Runtime;

/// <summary>An owned local reference. Dispose it before its environment or JNI callback returns.</summary>
public sealed class JavaLocalReference : IDisposable
{
    private readonly JavaEnvironment _environment;
    private nint _handle;
    private bool _disposed;

    internal JavaLocalReference(JavaEnvironment environment, nint handle) { _environment = environment; _handle = handle; }
    public nint Handle { get { ObjectDisposedException.ThrowIf(_disposed, this); _environment.EnsureAccessible(); return _handle; } }
    public JavaGlobalReference ToGlobal() => _environment.Promote(Handle);

    public void Dispose()
    {
        if (_disposed)
            return;
        _environment.EnsureAccessible();
        if (_handle != 0)
            _environment.DeleteLocal(_handle);
        _handle = 0;
        _disposed = true;
    }
}

/// <summary>An owned global reference, usable from attached threads while its JVM is alive.</summary>
public sealed class JavaGlobalReference : IDisposable
{
    private readonly JavaVirtualMachine _machine;
    private nint _handle;
    internal JavaGlobalReference(JavaVirtualMachine machine, nint handle) { _machine = machine; _handle = handle; }
    public nint Handle => _handle != 0 ? _handle : throw new ObjectDisposedException(nameof(JavaGlobalReference));

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

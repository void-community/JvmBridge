namespace JvmBridge.Runtime;

/// <summary>An owned local reference. Dispose it before its environment or JNI callback returns.</summary>
public sealed class JavaLocalReference : IDisposable
{
    private readonly JavaEnvironment _environment;
    private nint _handle;
    private bool _disposed;

    internal JavaLocalReference(JavaEnvironment environment, nint handle) { _environment = environment; _handle = handle; }

    /// <summary>Gets the raw local-reference handle after validating its thread and scope.</summary>
    public nint Handle { get { ObjectDisposedException.ThrowIf(_disposed, this); _environment.EnsureAccessible(); return _handle; } }

    /// <summary>Promotes this local reference to an owned global reference.</summary>
    /// <returns>An owned global reference that remains valid across attached threads while its JVM is alive.</returns>
    public JavaGlobalReference ToGlobal() => _environment.Promote(Handle);

    /// <inheritdoc/>
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

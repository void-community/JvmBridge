namespace JvmBridge.Runtime;

/// <summary>An owned weak global reference that does not keep its referent alive. Dispose while its JVM remains alive.</summary>
public sealed class JavaWeakGlobalReference : IDisposable
{
    private readonly JavaVirtualMachine _machine;
    private readonly object _sync = new();
    private nint _handle;

    internal JavaWeakGlobalReference(JavaVirtualMachine machine, nint handle) { _machine = machine; _handle = handle; }

    /// <summary>Gets the raw weak handle while owned. A nonzero handle does not imply a live referent.</summary>
    public nint Handle
    {
        get
        {
            lock (_sync)
                return _handle != 0 ? _handle : throw new ObjectDisposedException(nameof(JavaWeakGlobalReference));
        }
    }

    /// <summary>Promotes the referent to an owned local reference on the current attached thread.</summary>
    /// <param name="environment">An active environment belonging to the same JVM.</param>
    /// <returns>A strong local reference for this environment's scope, or <see langword="null"/> if collected.</returns>
    /// <remarks>Do not test the weak handle for collection and then use it: collection may race that test. Use the returned local throughout the operation. A pending Java exception is thrown, not treated as collection.</remarks>
    public JavaLocalReference? TryPromote(JavaEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            if (environment.GetVirtualMachine().Handle != _machine.Handle)
                throw new InvalidOperationException("The weak reference belongs to a different JVM.");
            return environment.TryPromoteWeak(_handle);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_handle == 0)
                return;
            using JavaThreadAttachment attachment = _machine.AttachCurrentThread();
            attachment.Environment.DeleteWeakGlobal(_handle);
            _handle = 0;
        }
    }
}

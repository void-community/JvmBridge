namespace JvmBridge.Runtime;

/// <summary>Detaches only threads attached by this scope. Dispose on the creating thread.</summary>
public sealed class JavaThreadAttachment : IDisposable
{
    private readonly int _threadIdentifier = System.Environment.CurrentManagedThreadId;
    private readonly JavaVirtualMachine _machine;
    private readonly bool _attached;
    private bool _disposed;

    /// <summary>Gets the JNI environment borrowed for the current thread and attachment scope.</summary>
    public JavaEnvironment Environment { get; }
    internal JavaThreadAttachment(JavaVirtualMachine machine, JavaEnvironment environment, bool attached) { _machine = machine; Environment = environment; _attached = attached; }
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        if (_threadIdentifier != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Thread attachments must be disposed on their creating thread.");
        if (_attached)
            _machine.DetachCurrentThread();
        Environment.Dispose();
        _disposed = true;
    }
}

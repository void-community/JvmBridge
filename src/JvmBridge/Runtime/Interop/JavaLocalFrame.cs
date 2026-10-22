using JvmBridge.Native;

namespace JvmBridge.Runtime;

/// <summary>A JNI local reference frame. Local references created while it is open are released together when it is disposed, so a loop over many objects does not exhaust the local reference table.</summary>
public sealed unsafe class JavaLocalFrame : IDisposable
{
    private readonly JavaEnvironment _environment;
    private bool _popped;

    internal JavaLocalFrame(JavaEnvironment environment)
    {
        _environment = environment;
    }

    /// <summary>Closes the frame, keeping one reference alive in the enclosing frame.</summary>
    /// <param name="result">A reference created in this frame, or 0 to keep nothing.</param>
    /// <returns>The kept reference, which belongs to the enclosing frame and which the caller owns.</returns>
    public JavaLocalReference Pop(nint result)
    {
        ObjectDisposedException.ThrowIf(_popped, this);
        _popped = true;
        _jobject* kept = _environment.Functions->PopLocalFrame((JNINativeInterface_**)_environment.Handle, (_jobject*)result);

        return new JavaLocalReference(_environment, (nint)kept);
    }

    /// <summary>Closes the frame and releases every local reference created in it.</summary>
    public void Dispose()
    {
        if (_popped)
            return;

        _popped = true;
        _ = _environment.Functions->PopLocalFrame((JNINativeInterface_**)_environment.Handle, null);
    }
}

using JvmBridge.Native;

namespace JvmBridge.Runtime;

/// <summary>A Java monitor that the current thread holds. Dispose it on the same thread to release the monitor; entering a monitor more than once needs the same number of disposals.</summary>
public sealed unsafe class JavaMonitor : IDisposable
{
    private readonly JavaEnvironment _environment;
    private readonly nint _target;
    private bool _released;

    internal JavaMonitor(JavaEnvironment environment, nint target)
    {
        _environment = environment;
        _target = target;
    }

    /// <summary>Releases the monitor. It never throws, so a failure to release is only visible as a monitor that stays held.</summary>
    public void Dispose()
    {
        if (_released)
            return;

        _released = true;
        _ = _environment.Functions->MonitorExit((JNINativeInterface_**)_environment.Handle, (_jobject*)_target);
    }
}

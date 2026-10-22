using JvmBridge.Native;

namespace JvmBridge.Runtime;

/// <summary>Java monitors for a <see cref="JavaEnvironment"/>, the lock that <c>synchronized</c> takes.</summary>
public static unsafe class JavaMonitors
{
    /// <summary>Enters the monitor of an object, blocking until the current thread owns it.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="target">A reference to the object whose monitor to enter; it must stay valid until the monitor is released.</param>
    /// <returns>The held monitor; dispose it on the same thread to release it.</returns>
    public static JavaMonitor MonitorEnter(this JavaEnvironment environment, nint target)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentOutOfRangeException.ThrowIfZero(target);
        environment.EnsureAccessible();
        int result = environment.Functions->MonitorEnter((JNINativeInterface_**)environment.Handle, (_jobject*)target);
        environment.ThrowIfException(nameof(MonitorEnter));

        if (result != 0)
            throw new JniException(result, operation: nameof(MonitorEnter));

        return new JavaMonitor(environment, target);
    }
}

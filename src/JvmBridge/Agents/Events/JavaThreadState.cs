namespace JvmBridge.Agents;

/// <summary>The state bits of a Java thread, as <see cref="AgentContext.GetThreadState"/> returns them.</summary>
[Flags]
public enum JavaThreadState
{
    /// <summary>The thread has not started or has ended, unless <see cref="Terminated"/> is set.</summary>
    None = 0,

    /// <summary>The thread is alive.</summary>
    Alive = 0x0001,

    /// <summary>The thread has ended.</summary>
    Terminated = 0x0002,

    /// <summary>The thread is runnable.</summary>
    Runnable = 0x0004,

    /// <summary>The thread is waiting for a monitor, a sleep or a notification.</summary>
    Waiting = 0x0080,

    /// <summary>The thread waits without a timeout.</summary>
    WaitingIndefinitely = 0x0010,

    /// <summary>The thread waits with a timeout.</summary>
    WaitingWithTimeout = 0x0020,

    /// <summary>The thread is in <c>Thread.sleep</c>.</summary>
    Sleeping = 0x0040,

    /// <summary>The thread is in <c>Object.wait</c>.</summary>
    InObjectWait = 0x0100,

    /// <summary>The thread is parked.</summary>
    Parked = 0x0200,

    /// <summary>The thread is blocked entering a monitor.</summary>
    BlockedOnMonitorEnter = 0x0400,

    /// <summary>The thread is suspended.</summary>
    Suspended = 0x100000,

    /// <summary>The thread was interrupted.</summary>
    Interrupted = 0x200000,

    /// <summary>The thread is running native code.</summary>
    InNative = 0x400000
}

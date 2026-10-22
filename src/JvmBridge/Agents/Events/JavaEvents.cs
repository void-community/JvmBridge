namespace JvmBridge.Agents;

/// <summary>The JVM events an agent can subscribe to with <see cref="AgentEvents.Subscribe"/>. Each one calls the matching virtual method of <see cref="JavaAgent"/>.</summary>
[Flags]
public enum JavaEvents
{
    /// <summary>No event.</summary>
    None = 0,

    /// <summary>A thread is about to run: <see cref="JavaAgent.OnThreadStart"/>.</summary>
    ThreadStart = 1 << 0,

    /// <summary>A thread is about to end: <see cref="JavaAgent.OnThreadEnd"/>.</summary>
    ThreadEnd = 1 << 1,

    /// <summary>A class was loaded, before it is prepared: <see cref="JavaAgent.OnClassLoad"/>.</summary>
    ClassLoad = 1 << 2,

    /// <summary>A Java exception was thrown: <see cref="JavaAgent.OnException"/>. Needs <see cref="JavaCapability.GenerateExceptionEvents"/>.</summary>
    Exception = 1 << 3,

    /// <summary>A method is about to run: <see cref="JavaAgent.OnMethodEntry"/>. Needs <see cref="JavaCapability.GenerateMethodEntryEvents"/> and slows the JVM a great deal.</summary>
    MethodEntry = 1 << 4,

    /// <summary>A method returned or was left by an exception: <see cref="JavaAgent.OnMethodExit"/>. Needs <see cref="JavaCapability.GenerateMethodExitEvents"/> and slows the JVM a great deal.</summary>
    MethodExit = 1 << 5,

    /// <summary>A garbage collection is about to start: <see cref="JavaAgent.OnGarbageCollectionStart"/>. Needs <see cref="JavaCapability.GenerateGarbageCollectionEvents"/>.</summary>
    GarbageCollectionStart = 1 << 6,

    /// <summary>A garbage collection finished: <see cref="JavaAgent.OnGarbageCollectionFinish"/>. Needs <see cref="JavaCapability.GenerateGarbageCollectionEvents"/>.</summary>
    GarbageCollectionFinish = 1 << 7,

    /// <summary>A thread is about to block on a monitor another thread holds: <see cref="JavaAgent.OnMonitorContendedEnter"/>. Needs <see cref="JavaCapability.GenerateMonitorEvents"/>.</summary>
    MonitorContendedEnter = 1 << 8,

    /// <summary>A thread acquired a monitor after blocking on it: <see cref="JavaAgent.OnMonitorContendedEntered"/>. Needs <see cref="JavaCapability.GenerateMonitorEvents"/>.</summary>
    MonitorContendedEntered = 1 << 9
}

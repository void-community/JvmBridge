using System.Numerics;

using JvmBridge.Native;

namespace JvmBridge.Agents;

/// <summary>
/// The JVM events that an agent receives. JvmBridge owns the single JVMTI callback table, so an agent never calls <c>SetEventCallbacks</c>: it subscribes here and overrides the matching methods of <see cref="JavaAgent"/>.
/// Subscribing requests the capabilities an event needs and reports a denial instead of throwing. A subscription made in <see cref="JavaAgent.Configure"/> takes effect when the callbacks are installed, and one made later takes effect at once.
/// An event whose handler throws repeatedly is unsubscribed, so a failing handler cannot flood the log or slow every method call.
/// </summary>
public sealed class AgentEvents
{
    private const int FailureLimit = 20;

    private static readonly EventInfo[] Known =
    [
        new(JavaEvents.ThreadStart, jvmtiEvent.JVMTI_EVENT_THREAD_START, Capability: null),
        new(JavaEvents.ThreadEnd, jvmtiEvent.JVMTI_EVENT_THREAD_END, Capability: null),
        new(JavaEvents.ClassLoad, jvmtiEvent.JVMTI_EVENT_CLASS_LOAD, Capability: null),
        new(JavaEvents.Exception, jvmtiEvent.JVMTI_EVENT_EXCEPTION, JavaCapability.GenerateExceptionEvents),
        new(JavaEvents.MethodEntry, jvmtiEvent.JVMTI_EVENT_METHOD_ENTRY, JavaCapability.GenerateMethodEntryEvents),
        new(JavaEvents.MethodExit, jvmtiEvent.JVMTI_EVENT_METHOD_EXIT, JavaCapability.GenerateMethodExitEvents),
        new(JavaEvents.GarbageCollectionStart, jvmtiEvent.JVMTI_EVENT_GARBAGE_COLLECTION_START, JavaCapability.GenerateGarbageCollectionEvents),
        new(JavaEvents.GarbageCollectionFinish, jvmtiEvent.JVMTI_EVENT_GARBAGE_COLLECTION_FINISH, JavaCapability.GenerateGarbageCollectionEvents),
        new(JavaEvents.MonitorContendedEnter, jvmtiEvent.JVMTI_EVENT_MONITOR_CONTENDED_ENTER, JavaCapability.GenerateMonitorEvents),
        new(JavaEvents.MonitorContendedEntered, jvmtiEvent.JVMTI_EVENT_MONITOR_CONTENDED_ENTERED, JavaCapability.GenerateMonitorEvents)
    ];

    private readonly AgentContext _context;
    private readonly int[] _failures = new int[32];
    private readonly object _sync = new();
    private JavaEvents _subscribed;

    internal AgentEvents(AgentContext context)
    {
        _context = context;
    }

    /// <summary>Gets the events that are subscribed.</summary>
    public JavaEvents Subscribed
    {
        get
        {
            lock (_sync)
                return _subscribed;
        }
    }

    /// <summary>Subscribes to events, requesting the capabilities they need. An event that the JVM cannot offer is reported in the result and does not stop the others.</summary>
    /// <param name="events">The events to subscribe to.</param>
    /// <returns>One status per requested event.</returns>
    public EventSubscriptionResult Subscribe(JavaEvents events)
    {
        List<EventSubscriptionStatus> statuses = [];

        foreach (EventInfo info in Known.Where(candidate => (events & candidate.Event) != 0))
        {
            if (info.Capability is { } capability && !_context.IsGranted(capability) && !_context.TryRequest(capability))
            {
                statuses.Add(new EventSubscriptionStatus(info.Event, Enabled: false, $"the JVM does not offer {capability} in its current phase"));

                continue;
            }

            lock (_sync)
                _subscribed |= info.Event;

            if (_context.EventsEnabled)
                _context.SetEventEnabled(info.Notification, enabled: true);

            statuses.Add(new EventSubscriptionStatus(info.Event, Enabled: true, Reason: null));
        }

        return new EventSubscriptionResult(statuses);
    }

    /// <summary>Stops receiving events. Events that are not subscribed are ignored.</summary>
    /// <param name="events">The events to unsubscribe from.</param>
    public void Unsubscribe(JavaEvents events)
    {
        foreach (EventInfo info in Known.Where(candidate => (events & candidate.Event) != 0))
        {
            bool wasSubscribed;

            lock (_sync)
            {
                wasSubscribed = (_subscribed & info.Event) != 0;
                _subscribed &= ~info.Event;
            }

            if (wasSubscribed && _context.EventsEnabled)
                _context.SetEventEnabled(info.Notification, enabled: false);
        }
    }

    /// <summary>Enables what was subscribed before the callbacks were installed.</summary>
    internal void EnablePending()
    {
        JavaEvents pending = Subscribed;

        foreach (EventInfo info in Known.Where(candidate => (pending & candidate.Event) != 0))
            _context.SetEventEnabled(info.Notification, enabled: true);
    }

    /// <summary>Counts a handler failure, logs the first one, and unsubscribes the event when failures keep coming. Always returns <see langword="true"/> so that a catch filter contains the exception.</summary>
    internal bool Failed(JavaEvents which, Exception exception)
    {
        int index = BitOperations.TrailingZeroCount((int)which);
        int count = Interlocked.Increment(ref _failures[index]);

        if (count == 1)
            AgentLog.Write(AgentLogLevel.Error, _context.Name, $"the {which} handler failed: {exception.GetType().Name}: {exception.Message}");

        if (count == FailureLimit)
        {
            AgentLog.Write(AgentLogLevel.Error, _context.Name, $"{which} was unsubscribed after {FailureLimit} failed calls in a row");
            Unsubscribe(which);
        }

        return true;
    }

    /// <summary>Records that a handler finished, which ends a run of failures.</summary>
    internal void Succeeded(JavaEvents which)
    {
        int index = BitOperations.TrailingZeroCount((int)which);

        if (Volatile.Read(ref _failures[index]) != 0)
            Volatile.Write(ref _failures[index], 0);
    }

    private sealed record EventInfo(JavaEvents Event, jvmtiEvent Notification, JavaCapability? Capability);
}

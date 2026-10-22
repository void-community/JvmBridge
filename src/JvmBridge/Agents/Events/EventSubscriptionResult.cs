namespace JvmBridge.Agents;

/// <summary>The outcome of <see cref="AgentEvents.Subscribe"/>: one status per requested event. A JVM that does not offer a capability denies its events without an exception.</summary>
public sealed class EventSubscriptionResult
{
    internal EventSubscriptionResult(IReadOnlyList<EventSubscriptionStatus> events)
    {
        Events = events;
    }

    /// <summary>Gets whether every requested event is subscribed.</summary>
    public bool AllEnabled => Events.All(status => status.Enabled);

    /// <summary>Gets one status per requested event.</summary>
    public IReadOnlyList<EventSubscriptionStatus> Events { get; }

    /// <summary>Describes the outcome in one line suitable for a log.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        string[] denied = [.. Events.Where(status => !status.Enabled).Select(status => $"{status.Event}: {status.Reason}")];

        return denied.Length == 0 ? $"{Events.Count} event{(Events.Count == 1 ? string.Empty : "s")} subscribed" : "not subscribed: " + string.Join("; ", denied);
    }
}

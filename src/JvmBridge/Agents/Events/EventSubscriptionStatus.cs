namespace JvmBridge.Agents;

/// <summary>What happened to one event in <see cref="AgentEvents.Subscribe"/>.</summary>
/// <param name="Event">The event.</param>
/// <param name="Enabled">Whether the event is subscribed. A subscription made in <see cref="JavaAgent.Configure"/> is enabled when the callbacks are installed.</param>
/// <param name="Reason">Why the event is not subscribed, or <see langword="null"/> when it is.</param>
public readonly record struct EventSubscriptionStatus(JavaEvents Event, bool Enabled, string? Reason);

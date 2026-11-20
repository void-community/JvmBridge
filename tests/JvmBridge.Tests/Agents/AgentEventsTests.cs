using System.Collections.Concurrent;

using JvmBridge.Agents;
using JvmBridge.Native;
using JvmBridge.Runtime;

using Xunit;

namespace JvmBridge.Tests.Agents;

/// <summary>Verifies the single callback table, capability negotiation and the subscriptions that decide which events the JVM delivers.</summary>
[Collection(FakeJvm.CollectionName)]
public sealed unsafe class AgentEventsTests
{
    /// <summary>A subscription made before the callbacks are installed waits, and is enabled when they are.</summary>
    [Fact]
    public void ConfigureTimeSubscriptionsAreEnabledWhenTheTableIsInstalled()
    {
        using FakeJvm jvm = new();

        SubscribingAgent agent = new(JavaEvents.ThreadStart | JavaEvents.ClassLoad);
        int started = AgentRuntime.Start(() => agent, jvm.Machine, options: 0, attached: false);

        Assert.Equal(Methods.JNI_OK, started);
        Assert.Contains(new EventToggle(jvmtiEvent.JVMTI_EVENT_THREAD_START, Enabled: true), FakeJvm.Toggles);
        Assert.Contains(new EventToggle(jvmtiEvent.JVMTI_EVENT_CLASS_LOAD, Enabled: true), FakeJvm.Toggles);
        Assert.Equal(JavaEvents.ThreadStart | JavaEvents.ClassLoad, agent.Subscribed);
    }

    /// <summary>A handler that keeps failing is unsubscribed after a run of failures, with one line for the first failure of each run and one for the unsubscription; a success ends the run.</summary>
    /// <exception cref="InvalidOperationException">The agent did not start, so it has no context whose events could be failed.</exception>
    [Fact]
    public void FailingHandlersAreUnsubscribed()
    {
        ConcurrentQueue<string> lines = [];
        AgentLogSink previous = AgentLog.Sink;

        try
        {
            AgentLog.Sink = (level, source, message) => lines.Enqueue(level.ToString() + "|" + source + "|" + message);

            using FakeJvm jvm = new();

            SubscribingAgent agent = new(JavaEvents.ThreadStart);
            int started = AgentRuntime.Start(() => agent, jvm.Machine, options: 0, attached: false);

            AgentEvents events = agent.Context?.Events
                ?? throw new InvalidOperationException(message: "The agent did not start.");

            InvalidOperationException failure = new(message: "handler failed");

            for (int count = 0; count < 19; count++)
                Assert.True(events.Failed(JavaEvents.ThreadStart, failure));

            events.Succeeded(JavaEvents.ThreadStart);

            for (int count = 0; count < 19; count++)
                Assert.True(events.Failed(JavaEvents.ThreadStart, failure));

            // Nineteen failures after the success are one short of the limit, so the handler is still subscribed.
            Assert.Equal(JavaEvents.ThreadStart, events.Subscribed);

            // The twentieth failure in a row reaches the limit.
            Assert.True(events.Failed(JavaEvents.ThreadStart, failure));

            // The event is switched off in the JVM too, and the log has the first failure of each run plus the unsubscription.
            Assert.Equal(Methods.JNI_OK, started);
            Assert.Equal(JavaEvents.None, events.Subscribed);
            Assert.Contains(new EventToggle(jvmtiEvent.JVMTI_EVENT_THREAD_START, Enabled: false), FakeJvm.Toggles);

            Assert.Equal(expected: 3,
                lines.Count(line => line.StartsWith(value: "Error|", StringComparison.Ordinal) && line.Contains(value: "ThreadStart", StringComparison.Ordinal)));
        }
        finally
        {
            AgentLog.Sink = previous;
        }
    }

    /// <summary>
    /// The Java major version comes from the VM's own specification version. Java 6 and 7 report 1.0 there, which says nothing before the JVM is live, so the version stays 0 and is asked again instead of keeping that 0; once
    /// known, it no longer changes.
    /// </summary>
    [Fact]
    public void JavaMajorIsReadAgainUntilTheJvmSaysIt()
    {
        using FakeJvm jvm = new();

        AgentContext context = jvm.CreateContext(name: "early");

        // With no version, and with the 1.0 that a JVM reports before it is live, the major version is still unknown.
        Assert.Equal(expected: 0, context.JavaMajor);
        FakeJvm.ReportSpecification(version: "1.0");
        Assert.Equal(expected: 0, context.JavaMajor);

        // The first version that says something is kept, and a different one afterwards does not replace it.
        FakeJvm.ReportSpecification(version: "1.8");
        Assert.Equal(expected: 8, context.JavaMajor);
        FakeJvm.ReportSpecification(version: "21");
        Assert.Equal(expected: 8, context.JavaMajor);
    }

    /// <summary>The Java major version comes from the specification version, which Java 8 and earlier write as 1.x.</summary>
    [Fact]
    public void JavaMajorReadsBothForms()
    {
        // Java 8 and earlier write the major version after "1.", and later versions write it alone.
        Assert.Equal(expected: 6, AgentContext.ParseJavaMajor(specification: "1.6"));
        Assert.Equal(expected: 8, AgentContext.ParseJavaMajor(specification: "1.8"));
        Assert.Equal(expected: 21, AgentContext.ParseJavaMajor(specification: "21"));

        // A missing or unreadable specification version gives 0, which stands for a version that is not known.
        Assert.Equal(expected: 0, AgentContext.ParseJavaMajor(specification: null));
        Assert.Equal(expected: 0, AgentContext.ParseJavaMajor(specification: "soon"));
    }

    /// <summary>A capability the JVM offers is granted and one it does not is refused, without an exception.</summary>
    [Fact]
    public void RequestsGrantOnlyWhatTheJvmOffers()
    {
        using FakeJvm jvm = new();

        FakeJvm.Offer(JavaCapability.RedefineClasses, JavaCapability.RetransformClasses);

        AgentContext context = jvm.CreateContext(name: "capabilities");

        // A capability the JVM offers is granted when it is requested.
        Assert.True(context.TryRequest(JavaCapability.RedefineClasses));
        Assert.True(context.IsGranted(JavaCapability.RedefineClasses));

        // A capability the JVM does not offer is refused by the return value alone.
        Assert.False(context.TryRequest(JavaCapability.GenerateMonitorEvents));
        Assert.False(context.IsGranted(JavaCapability.GenerateMonitorEvents));

        // An offered capability is not held until it is requested, which is what retransformation depends on.
        Assert.False(context.CanRetransform);
        Assert.True(context.TryRequest(JavaCapability.RetransformClasses));
        Assert.True(context.CanRetransform);
        Assert.True(context.IsGranted(JavaCapability.RetransformClasses));
    }

    /// <summary>Starting an agent installs one table in which every event the agent can subscribe to has a callback, and the events nobody can subscribe to have none.</summary>
    [Fact]
    public void StartInstallsTheWholeCallbackTable()
    {
        using FakeJvm jvm = new();

        int started = AgentRuntime.Start(static () => new SubscribingAgent(JavaEvents.None),
            jvm.Machine,
            options: 0,
            attached: false);

        jvmtiEventCallbacks callbacks = FakeJvm.Callbacks;

        // The start succeeds and installs the callbacks of the JVM life cycle, class and thread events.
        Assert.Equal(Methods.JNI_OK, started);
        Assert.True(callbacks.VMInit != null && callbacks.VMDeath != null && callbacks.ClassPrepare != null && callbacks.ClassFileLoadHook != null);
        Assert.True(callbacks.ThreadStart != null && callbacks.ThreadEnd != null && callbacks.ClassLoad != null && callbacks.Exception != null);

        // The method, garbage collection and monitor events, which need capabilities, have callbacks as well.
        Assert.True(callbacks.MethodEntry != null && callbacks.MethodExit != null && callbacks.GarbageCollectionStart != null && callbacks.GarbageCollectionFinish != null);
        Assert.True(callbacks.MonitorContendedEnter != null && callbacks.MonitorContendedEntered != null);

        // The debugging events, which no agent can subscribe to, have none.
        Assert.True(callbacks.SingleStep == null && callbacks.Breakpoint == null && callbacks.FramePop == null && callbacks.FieldAccess == null);
    }

    /// <summary>Events that need a capability the JVM does not offer are denied with the reason, the others are subscribed, and nothing throws.</summary>
    [Fact]
    public void SubscribeReportsDeniedEvents()
    {
        using FakeJvm jvm = new();

        FakeJvm.Offer(JavaCapability.GenerateExceptionEvents);

        AgentContext context = jvm.CreateContext(name: "denials");

        EventSubscriptionResult subscription = context.Events
            .Subscribe(JavaEvents.Exception | JavaEvents.MonitorContendedEnter | JavaEvents.ThreadStart);

        Assert.False(subscription.AllEnabled);
        Assert.Equal(expected: 3, subscription.Events.Count);
        Assert.Equal(JavaEvents.Exception | JavaEvents.ThreadStart, context.Events.Subscribed);

        EventSubscriptionStatus denied = subscription.Events.Single(static status => !status.Enabled);

        Assert.Equal(JavaEvents.MonitorContendedEnter, denied.Event);
        Assert.Contains(expectedSubstring: "GenerateMonitorEvents", denied.Reason, StringComparison.Ordinal);

        Assert.StartsWith(expectedStartString: "not subscribed: MonitorContendedEnter",
            subscription.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>Subscribing after the agent started enables the event at once, and unsubscribing disables it.</summary>
    /// <exception cref="InvalidOperationException">The agent did not start, so it has no context whose events could be subscribed.</exception>
    [Fact]
    public void SubscriptionsAfterStartTakeEffectAtOnce()
    {
        using FakeJvm jvm = new();

        SubscribingAgent agent = new(JavaEvents.None);
        int started = AgentRuntime.Start(() => agent, jvm.Machine, options: 0, attached: false);
        AgentEvents events = agent.Context?.Events ?? throw new InvalidOperationException(message: "The agent did not start.");
        EventSubscriptionResult subscribed = events.Subscribe(JavaEvents.ThreadEnd);

        events.Unsubscribe(JavaEvents.ThreadEnd);
        Assert.Equal(Methods.JNI_OK, started);
        Assert.True(subscribed.AllEnabled);
        Assert.Equal(JavaEvents.None, events.Subscribed);

        Assert.Equal(expected:
        [
            new EventToggle(jvmtiEvent.JVMTI_EVENT_THREAD_END, Enabled: true),
            new EventToggle(jvmtiEvent.JVMTI_EVENT_THREAD_END, Enabled: false),
        ],
            FakeJvm.Toggles.Skip(FakeJvm.Toggles.Count - 2));
    }

    /// <summary>The thread state bits come back as flags.</summary>
    [Fact]
    public void ThreadStateComesBackAsFlags()
    {
        using FakeJvm jvm = new();

        FakeJvm.ReportThread(JavaThreadState.Alive | JavaThreadState.Runnable | JavaThreadState.InNative);

        AgentContext context = jvm.CreateContext(name: "state");
        JavaThreadState state = context.GetThreadState(thread: 0);

        Assert.Equal(JavaThreadState.Alive | JavaThreadState.Runnable | JavaThreadState.InNative, state);
        Assert.NotEqual(JavaThreadState.None, state & JavaThreadState.Runnable);
        Assert.Equal(JavaThreadState.None, state & JavaThreadState.Waiting);
    }

    /// <summary>A JVM that refuses JVMTI 1.2, as early Java 6 builds do, gets an agent through 1.1, and one that refuses every version fails the start with the versions it was asked for.</summary>
    [Fact]
    public void ToolingFallsBackToOlderVersions()
    {
        using FakeJvm jvm = new();

        FakeJvm.OfferToolingUpTo(unchecked((int)Methods.JVMTI_VERSION_1_1));

        AgentContext context = jvm.CreateContext(name: "java6");

        Assert.Equal(expected: [unchecked((int)Methods.JVMTI_VERSION_1_2), unchecked((int)Methods.JVMTI_VERSION_1_1)], FakeJvm.ToolingRequests);
        FakeJvm.OfferToolingUpTo(version: 0);

        JniException failure = Assert.Throws<JniException>(() => jvm.CreateContext(name: "none"));

        Assert.Equal(Methods.JNI_EVERSION, failure.ErrorCode);
        Assert.Contains(expectedSubstring: "JVMTI 1.2, 1.1 or 1.0", failure.Message, StringComparison.Ordinal);
        Assert.Equal(expected: "java6", context.Name);
    }

    private sealed class SubscribingAgent(JavaEvents events) : JavaAgent
    {
        internal JavaEvents Subscribed => Context?.Events.Subscribed ?? JavaEvents.None;

        internal AgentContext? Context { get; private set; }

        public override void Configure(AgentContext context)
        {
            Context = context;

            if (events != JavaEvents.None)
            {
                EventSubscriptionResult subscription = context.Events.Subscribe(events);

                if (!subscription.AllEnabled)
                    throw new InvalidOperationException(subscription.ToString());
            }
        }
    }
}

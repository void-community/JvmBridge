# JVM events

JvmBridge owns the one JVMTI callback table, so an agent never calls `SetEventCallbacks`. Subscribe to the events you want and override the matching method of `JavaAgent`:

```csharp
public sealed class WatchAgent : JavaAgent
{
    public override void Configure(AgentContext context)
    {
        EventSubscriptionResult result = context.Events.Subscribe(JavaEvents.ThreadStart | JavaEvents.GarbageCollectionFinish | JavaEvents.Exception);

        if (!result.AllEnabled)
            context.Log(AgentLogLevel.Warning, result.ToString());
    }

    public override void OnThreadStart(AgentContext context, JavaEnvironment environment, nint thread)
    {
        context.Log(AgentLogLevel.Debug, "thread " + context.GetThreadInfo(environment, thread).Name);
    }

    public override void OnGarbageCollectionFinish(AgentContext context)
    {
        // no JNI here: this runs on a JVM thread while the world is stopped
    }
}
```

| Event | Override | Needs |
|---|---|---|
| `ThreadStart`, `ThreadEnd` | `OnThreadStart`, `OnThreadEnd` | nothing |
| `ClassLoad` | `OnClassLoad` | nothing |
| `Exception` | `OnException` | `GenerateExceptionEvents` |
| `MethodEntry`, `MethodExit` | `OnMethodEntry`, `OnMethodExit` | `GenerateMethodEntryEvents`, `GenerateMethodExitEvents` |
| `GarbageCollectionStart`, `GarbageCollectionFinish` | `OnGarbageCollectionStart`, `OnGarbageCollectionFinish` | `GenerateGarbageCollectionEvents` |
| `MonitorContendedEnter`, `MonitorContendedEntered` | `OnMonitorContendedEnter`, `OnMonitorContendedEntered` | `GenerateMonitorEvents` |

## Capabilities and denials

`Subscribe` requests the capabilities an event needs and returns an `EventSubscriptionResult` with one status per event. A JVM that does not offer a capability denies its events with a reason, and the rest are still subscribed; nothing throws. Request a capability yourself with `context.TryRequest(JavaCapability.X)`, and check one with `IsGranted`.

What a JVM offers depends on how the agent was loaded. On HotSpot 8, 17 and 21, an agent loaded at startup with `-agentpath` was granted every capability above. An agent attached to a running JVM was granted thread, class-load, garbage-collection and monitor events and redefinition, but not exception events or method entry and exit. So:

- Subscribe, and request capabilities, in `Configure`. A subscription made there is enabled once the callbacks are installed.
- If you need method entry and exit only for a moment, request the capabilities in `Configure` and subscribe later around the call you want to trace; the capability stays granted and the events cost nothing while they are off.
- Always read the result. An agent that must work attached has to cope with a denial.

## What each handler may do

- **Thread, class load, monitor events** receive a `JavaEnvironment` and a callback-local thread reference. Ordinary JNI is allowed, except where a Java exception is pending.
- **`OnException`** runs while the exception is pending, so use JVMTI wrappers and the JNI functions that are valid with a pending exception. `JavaExceptionEvent` carries the raw method IDs, bytecode indexes and the exception reference.
- **`OnMethodEntry` and `OnMethodExit`** run for every call and slow the JVM a great deal. Keep them short, and do not call Java from them: the runtime ignores events on a thread while one of its handlers runs, so a handler that calls Java does not recurse, but it also does not see those calls. Prefer [**method patches**](patching.md) for a few methods.
- **Garbage collection events** have no environment. They run on a JVM thread while the world is stopped: no JNI, no Java, no waiting. Counting with a plain increment is fine, taking a lock is not.
- A handler that throws is contained and logged. After 20 failures in a row the event is unsubscribed, with a log line, so a broken handler cannot flood the log or slow every call.

## Looking at threads, stacks and methods

```csharp
JavaThreadInfo info = context.GetThreadInfo(environment, thread);          // name, priority, daemon
JavaThreadState state = context.GetThreadState(thread);                    // Alive | Runnable | InNative ...
JavaStackFrame[] frames = context.GetStackTrace(environment, thread: 0, maxFrames: 16);   // 0 is the current thread
JavaMethodName name = context.DescribeMethod(environment, rawMethodId);    // class, name, descriptor
nint[] threads = context.GetAllThreads(environment);
```

The references that `GetAllThreads` returns belong to the current local reference scope and are released when it ends, for example when the native method that called it returns.

## Redefining classes

```csharp
context.TryRequest(JavaCapability.RedefineClasses);                         // in Configure
context.RedefineClasses([new ClassRedefinition(type, newBytes)]);           // later, with a class reference
```

Redefinition replaces the bytes of a loaded class and may only change method bodies. Combine it with a `ClassPatch` to change a constant: `ClassPatch.For(name).ReplaceConstant(...).Apply(bytes).Bytes` gives the new bytes. `RedefineClasses` throws `NotSupportedException` when the capability was not granted.

## Raw access

`context.RawHandle` stays available. Calling `SetEventCallbacks` through it replaces the table JvmBridge installed and silences every event above and the agent's own hooks, so use it only for JVMTI functions that JvmBridge does not wrap.

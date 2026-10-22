using System.Collections.Concurrent;
using System.Globalization;

using JvmBridge.Agents;
using JvmBridge.Runtime;

namespace HelloAgent;

/// <summary>
/// Java-callable helpers written in C#, which the fixture reaches by reflection. They exercise what an agent author uses beyond patching: JVM events, thread and stack wrappers, primitive and string arrays, raising a Java exception, monitors
/// and local frames, and redefining a class. The class is declared with <c>[JavaClass]</c>, so building the sample also builds the generator's array and exception marshalling with NativeAOT.
/// </summary>
[JavaClass("jvmbridge/sample/Interop")]
internal static partial class Interop
{
    private const int NameLimit = 2000;
    private static readonly EventCounter ClassLoads = new();
    private static readonly EventCounter Exceptions = new();
    private static readonly EventCounter MethodExits = new();
    private static readonly EventCounter MonitorEntered = new();
    private static readonly EventCounter MonitorEnters = new();
    private static readonly EventCounter ThreadEnds = new();
    private static readonly EventCounter ThreadStarts = new();
    private static readonly ConcurrentQueue<string> ThreadNames = [];
    private static readonly ConcurrentQueue<string> TracedMethods = [];
    private static int s_collectionFinishes;
    private static int s_collectionStarts;
    private static volatile bool s_tracing;

    internal static AgentContext? Context { get; set; }

    internal static string Subscription { get; set; } = string.Empty;

    internal static void ClassLoaded()
    {
        ClassLoads.Increment();
    }

    // Garbage collection callbacks run one at a time on a JVM thread while the world is stopped, so they count without locking or waiting.
    internal static void CollectionFinished()
    {
        Volatile.Write(ref s_collectionFinishes, Volatile.Read(ref s_collectionFinishes) + 1);
    }

    internal static void CollectionStarted()
    {
        Volatile.Write(ref s_collectionStarts, Volatile.Read(ref s_collectionStarts) + 1);
    }

    /// <summary>Summarizes the events received so far.</summary>
    [JavaMethod]
    internal static string Events()
    {
        string subscribed = Context?.Events.Subscribed.ToString().Replace(oldValue: ", ", newValue: "+", StringComparison.Ordinal) ?? string.Empty;

        return string.Join(
            separator: ";",
            "threadStart=" + ThreadStarts.Value.ToString(CultureInfo.InvariantCulture),
            "threadEnd=" + ThreadEnds.Value.ToString(CultureInfo.InvariantCulture),
            "classLoad=" + ClassLoads.Value.ToString(CultureInfo.InvariantCulture),
            "exception=" + Exceptions.Value.ToString(CultureInfo.InvariantCulture),
            "collectionStart=" + Volatile.Read(ref s_collectionStarts).ToString(CultureInfo.InvariantCulture),
            "collectionFinish=" + Volatile.Read(ref s_collectionFinishes).ToString(CultureInfo.InvariantCulture),
            "monitorEnter=" + MonitorEnters.Value.ToString(CultureInfo.InvariantCulture),
            "monitorEntered=" + MonitorEntered.Value.ToString(CultureInfo.InvariantCulture),
            "methodExit=" + MethodExits.Value.ToString(CultureInfo.InvariantCulture),
            "subscribed=" + subscribed,
            "subscription=" + Subscription
        );
    }

    internal static void ExceptionThrown()
    {
        Exceptions.Increment();
    }

    /// <summary>Holds an object's monitor and a local frame while it works, then releases both.</summary>
    [JavaMethod]
    internal static string Locked(JavaEnvironment environment, [JavaType("Ljava/lang/Object;")] nint target)
    {
        using JavaMonitor monitor = environment.MonitorEnter(target);

        using JavaLocalFrame frame = environment.PushLocalFrame(capacity: 8);

        using JavaLocalReference text = environment.NewString(value: "locked");

        return environment.GetString(text.Handle) ?? string.Empty;
    }

    internal static void MethodEntered(AgentContext context, JavaEnvironment environment, nint method)
    {
        if (s_tracing && TracedMethods.Count < NameLimit)
            TracedMethods.Enqueue(context.DescribeMethod(environment, method).ToString());
    }

    internal static void MethodExited()
    {
        MethodExits.Increment();
    }

    internal static void MonitorAcquired()
    {
        MonitorEntered.Increment();
    }

    internal static void MonitorBlocking()
    {
        MonitorEnters.Increment();
    }

    /// <summary>Returns a positive number or raises a Java exception.</summary>
    [JavaMethod]
    internal static int Positive(int value)
    {
        return value > 0 ? value : throw new JavaThrowException(javaClassName: "java/lang/IllegalArgumentException", "must be positive: " + value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Redefines a loaded class with its own bytes after a patch has changed a constant, so the new behavior shows without reloading anything.</summary>
    [JavaMethod]
    internal static string Redefine(JavaEnvironment environment, [JavaType("Ljava/lang/Class;")] nint type, byte[] bytes)
    {
        AgentContext context = Context ?? throw new InvalidOperationException(message: "The agent is not initialized.");

        if (!context.IsGranted(JavaCapability.RedefineClasses))
            return "unavailable";

        ClassPatchResult patched = ClassPatch.For(className: "BridgeFixture$Redefinable").ReplaceConstant(oldValue: "before", newValue: "after").Apply(bytes);

        if (patched.Bytes is not { } changed)
            return "patch failed: " + patched.SkippedReason;

        context.RedefineClasses([new ClassRedefinition(type, changed)]);

        return "ok";
    }

    /// <summary>Reverses a byte array, to check that arrays cross the boundary in both directions and that null stays null.</summary>
    [JavaMethod]
    internal static byte[]? Reverse(byte[]? values)
    {
        if (values is null)
            return null;

        byte[] reversed = [.. values];
        Array.Reverse(reversed);

        return reversed;
    }

    /// <summary>Returns the top frames of the calling thread's stack.</summary>
    [JavaMethod]
    internal static string Stack(JavaEnvironment environment)
    {
        AgentContext context = Context ?? throw new InvalidOperationException(message: "The agent is not initialized.");

        return string.Join(separator: ",", context.GetStackTrace(environment, thread: 0, maxFrames: 16).Select(static frame => frame.Method.ToString()));
    }

    internal static void ThreadEnded(AgentContext context, JavaEnvironment environment, nint thread)
    {
        ThreadEnds.Increment();
        Remember(context, environment, thread, kind: "end");
    }

    /// <summary>Returns the names of the threads that started and ended.</summary>
    [JavaMethod]
    internal static string ThreadLog()
    {
        return string.Join(separator: ",", ThreadNames);
    }

    internal static void ThreadStarted(AgentContext context, JavaEnvironment environment, nint thread)
    {
        ThreadStarts.Increment();
        Remember(context, environment, thread, kind: "start");
    }

    /// <summary>Returns the names of the live threads and the state of the calling thread.</summary>
    [JavaMethod]
    internal static string Threads(JavaEnvironment environment)
    {
        AgentContext context = Context ?? throw new InvalidOperationException(message: "The agent is not initialized.");
        nint[] threads = context.GetAllThreads(environment);
        List<string> names = [];

        // The thread references are local references of this native call, which the JVM releases when it returns.
        foreach (nint thread in threads)
            names.Add(context.GetThreadInfo(environment, thread).Name);

        return "names=" + string.Join(separator: ",", names) + ";state=" + context.GetThreadState(thread: 0).ToString();
    }

    /// <summary>Turns method entry and exit events on or off, and returns what the JVM said.</summary>
    [JavaMethod]
    internal static string Trace(bool on)
    {
        AgentContext context = Context ?? throw new InvalidOperationException(message: "The agent is not initialized.");

        if (!on)
        {
            s_tracing = false;
            context.Events.Unsubscribe(JavaEvents.MethodEntry | JavaEvents.MethodExit);

            return "off";
        }

        EventSubscriptionResult result = context.Events.Subscribe(JavaEvents.MethodEntry | JavaEvents.MethodExit);
        s_tracing = result.AllEnabled;

        return result.AllEnabled ? "on" : "denied: " + result;
    }

    /// <summary>Returns the methods seen while tracing was on.</summary>
    [JavaMethod]
    internal static string Traced()
    {
        return string.Join(separator: ",", TracedMethods);
    }

    /// <summary>Splits text into words, to check string arrays.</summary>
    [JavaMethod]
    internal static string[] Words(string text)
    {
        return text.Split(separator: ' ');
    }

    private static void Remember(AgentContext context, JavaEnvironment environment, nint thread, string kind)
    {
        if (ThreadNames.Count < NameLimit)
            ThreadNames.Enqueue(kind + ":" + context.GetThreadInfo(environment, thread).Name);
    }
}

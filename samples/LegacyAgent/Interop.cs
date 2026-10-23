using System.Collections.Concurrent;
using System.Globalization;

using JvmBridge.Agents;

namespace LegacyAgent;

/// <summary>Helpers written in C# that the fixture reaches: a call that a patch redirects here, and a summary of what the agent saw. The generator writes the class with the class file version of <c>JavaRelease</c>, 50.</summary>
[JavaClass("jvmbridge/legacy/Interop")]
internal static partial class Interop
{
    // Events arrive on any thread; each one adds a marker, so they are counted without locking.
    private static readonly ConcurrentQueue<byte> ClassLoads = [];
    private static readonly ConcurrentQueue<byte> ThreadStarts = [];

    internal static string Attached { get; set; } = "not run";

    internal static string Subscription { get; set; } = string.Empty;

    internal static void ClassLoaded()
    {
        ClassLoads.Enqueue(item: 1);
    }

    /// <summary>Summarizes the events received so far and what attaching a thread of the agent's own reported.</summary>
    [JavaMethod]
    internal static string Events()
    {
        return string.Join(
            separator: ";",
            "subscription=" + Subscription,
            "threadStart=" + ThreadStarts.Count.ToString(CultureInfo.InvariantCulture),
            "classLoad=" + ClassLoads.Count.ToString(CultureInfo.InvariantCulture),
            "attach=" + Attached
        );
    }

    /// <summary>Replaces <c>String.length()</c> in <c>LegacyFixture.length</c>.</summary>
    [JavaMethod]
    internal static int Length(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return 1000 + value.Length;
    }

    internal static void ThreadStarted()
    {
        ThreadStarts.Enqueue(item: 1);
    }
}

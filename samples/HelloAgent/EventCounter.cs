using System.Collections.Concurrent;

namespace HelloAgent;

/// <summary>Counts events from any thread without locking: each event adds a marker to a concurrent queue.</summary>
internal sealed class EventCounter
{
    private readonly ConcurrentQueue<byte> _events = [];

    internal int Value => _events.Count;

    internal void Increment()
    {
        _events.Enqueue(item: 1);
    }
}

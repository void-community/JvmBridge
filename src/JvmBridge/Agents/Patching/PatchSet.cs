namespace JvmBridge.Agents;

/// <summary>
/// An immutable set of registered patches. The class-file hook reads one set per class without locking, and registering or removing a patch builds a new set. Patches apply in registration order, whether they name a class or match
/// classes, so each carries the sequence number it was registered with.
/// </summary>
internal sealed class PatchSet
{
    private readonly Dictionary<string, Entry[]> _exact;
    private readonly Entry[] _matching;

    private PatchSet(Entry[] entries)
    {
        Entries = entries;
        _exact = entries.Where(entry => entry.Patch.Matcher.ExactName is not null).GroupBy(entry => entry.Patch.Matcher.ExactName ?? string.Empty).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        _matching = [.. entries.Where(entry => entry.Patch.Matcher.ExactName is null)];
        NeedsHierarchy = entries.Any(entry => entry.Patch.Matcher.NeedsHierarchy);
        NeedsAnnotations = entries.Any(entry => entry.Patch.Matcher.NeedsAnnotations);
    }

    internal static PatchSet Empty { get; } = new([]);

    /// <summary>Gets the number of patches.</summary>
    internal int Count => Entries.Length;

    /// <summary>Gets whether any patch looks at supertypes, so the runtime must learn them from every class that loads.</summary>
    internal bool NeedsHierarchy { get; }

    /// <summary>Gets whether any patch reads class-level annotations.</summary>
    internal bool NeedsAnnotations { get; }

    /// <summary>Gets the patches in registration order.</summary>
    internal IReadOnlyList<ClassPatch> Patches => [.. Entries.Select(entry => entry.Patch)];

    private Entry[] Entries { get; }

    /// <summary>Gets the patches whose name test accepts a class, in registration order. Header tests are not evaluated.</summary>
    internal IReadOnlyList<ClassPatch> Candidates(string className)
    {
        if (!_exact.TryGetValue(className, out Entry[]? named) && _matching.Length == 0)
            return [];

        IEnumerable<Entry> found = (named ?? []).Concat(_matching.Where(entry => entry.Patch.Matcher.MatchesName(className)));

        return [.. found.OrderBy(entry => entry.Sequence).Select(entry => entry.Patch)];
    }

    internal bool Contains(ClassPatch patch)
    {
        return Entries.Any(entry => ReferenceEquals(entry.Patch, patch));
    }

    internal PatchSet With(ClassPatch patch, long sequence)
    {
        return new PatchSet([.. Entries, new Entry(patch, sequence)]);
    }

    internal PatchSet Without(ClassPatch patch)
    {
        return new PatchSet([.. Entries.Where(entry => !ReferenceEquals(entry.Patch, patch))]);
    }

    private sealed record Entry(ClassPatch Patch, long Sequence);
}

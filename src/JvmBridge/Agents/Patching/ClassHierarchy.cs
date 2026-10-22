using JvmBridge.ClassFiles;

namespace JvmBridge.Agents;

/// <summary>
/// What is known about the supertypes of classes, so that <see cref="ClassPatch.ForSubclassesOf"/> can look beyond a class's direct superclass and interfaces. The runtime fills one from the class files it sees load and, when it needs one,
/// from the classes that were already loaded. A test can fill one by hand and pass it to <see cref="ClassPatch.Matches"/>. A supertype that was never recorded ends the walk up that branch, so a class whose ancestry runs through an unknown type does not match.
/// </summary>
public sealed class ClassHierarchy
{
    private readonly Dictionary<string, Supertypes> _known = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    /// <summary>Gets the number of classes recorded.</summary>
    public int Count
    {
        get
        {
            lock (_sync)
                return _known.Count;
        }
    }

    /// <summary>Records the direct supertypes of a class, replacing an earlier record.</summary>
    /// <param name="className">The internal name of the class.</param>
    /// <param name="superName">The internal name of its superclass, or <see langword="null"/> for none.</param>
    /// <param name="interfaces">The internal names of the interfaces it directly implements or extends.</param>
    public void Add(string className, string? superName, IEnumerable<string> interfaces)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(interfaces);
        Supertypes supertypes = new(superName, [.. interfaces]);

        lock (_sync)
            _known[className] = supertypes;
    }

    /// <summary>Gets whether the direct supertypes of a class are recorded.</summary>
    /// <param name="className">The internal name of the class.</param>
    /// <returns><see langword="true"/> when the class was recorded.</returns>
    public bool Contains(string className)
    {
        lock (_sync)
            return _known.ContainsKey(className);
    }

    /// <summary>Gets whether a recorded class extends or implements a type, directly or through other recorded types. A class is not a subtype of itself.</summary>
    /// <param name="className">The internal name of the class.</param>
    /// <param name="ancestor">The internal name of the superclass or interface.</param>
    /// <returns><see langword="true"/> when the walk from the class reaches the ancestor.</returns>
    public bool IsSubtypeOf(string className, string ancestor)
    {
        Supertypes? supertypes = Find(className);

        return supertypes is not null && IsSubtype(supertypes.Super, supertypes.Interfaces, ancestor);
    }

    internal bool IsSubtype(string? superName, IReadOnlyList<string> interfaces, string ancestor)
    {
        HashSet<string> visited = new(StringComparer.Ordinal);
        Stack<string> pending = [];

        if (superName is not null)
            pending.Push(superName);

        foreach (string name in interfaces)
            pending.Push(name);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            if (current == ancestor)
                return true;

            if (!visited.Add(current) || Find(current) is not { } next)
                continue;

            if (next.Super is not null)
                pending.Push(next.Super);

            foreach (string name in next.Interfaces)
                pending.Push(name);
        }

        return false;
    }

    internal void Record(ClassHeader header)
    {
        Supertypes supertypes = new(header.SuperName, [.. header.Interfaces]);

        lock (_sync)
            _known[header.Name] = supertypes;
    }

    private Supertypes? Find(string className)
    {
        lock (_sync)
            return _known.TryGetValue(className, out Supertypes? supertypes) ? supertypes : null;
    }

    private sealed record Supertypes(string? Super, string[] Interfaces);
}

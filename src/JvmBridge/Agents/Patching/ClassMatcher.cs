using JvmBridge.ClassFiles;

namespace JvmBridge.Agents;

/// <summary>
/// Decides which classes a <see cref="ClassPatch"/> targets, cheapest test first: the class name, then the supertypes, then the class-level annotations. Only the name test runs for every class that loads; the others need the class header
/// and run for the classes that pass the name test.
/// </summary>
internal sealed class ClassMatcher
{
    private readonly string[] _annotatedWith;
    private readonly Func<string, bool> _name;
    private readonly string[] _subtypeOf;

    private ClassMatcher(string description, string? exactName, Func<string, bool> name, string[] subtypeOf, string[] annotatedWith)
    {
        Description = description;
        ExactName = exactName;
        _name = name;
        _subtypeOf = subtypeOf;
        _annotatedWith = annotatedWith;
    }

    /// <summary>Gets a description for logs, for example <c>package com/acme</c>.</summary>
    internal string Description { get; }

    /// <summary>Gets the class name when the name test is an exact match, so the patch set can index it, or <see langword="null"/>.</summary>
    internal string? ExactName { get; }

    /// <summary>Gets whether the matcher reads class-level annotations.</summary>
    internal bool NeedsAnnotations => _annotatedWith.Length > 0;

    /// <summary>Gets whether the matcher needs the class header, for supertypes or annotations.</summary>
    internal bool NeedsHeader => NeedsHierarchy || NeedsAnnotations;

    /// <summary>Gets whether the matcher looks at supertypes.</summary>
    internal bool NeedsHierarchy => _subtypeOf.Length > 0;

    /// <summary>Gets whether the name test accepts every class, so only the header tests narrow it down.</summary>
    internal bool IsUnfilteredByName { get; private init; }

    internal static ClassMatcher Any()
    {
        return new ClassMatcher(description: "any class", exactName: null, static name => true, [], []) { IsUnfilteredByName = true };
    }

    internal static ClassMatcher Exact(string className)
    {
        return new ClassMatcher(className, className, name => name == className, [], []);
    }

    internal static ClassMatcher Filter(Func<string, bool> filter, string description)
    {
        return new ClassMatcher(description, exactName: null, filter, [], []);
    }

    internal static ClassMatcher Package(string package, bool includeSubpackages)
    {
        string prefix = package + "/";

        return new ClassMatcher(
            includeSubpackages ? $"package {package} and its subpackages" : $"package {package}",
            exactName: null,
            name => name.StartsWith(prefix, StringComparison.Ordinal) && (includeSubpackages || !name.AsSpan(prefix.Length).Contains('/')),
            [],
            []
        );
    }

    internal ClassMatcher AnnotatedWith(string annotation)
    {
        return new ClassMatcher($"{Description} annotated with {annotation}", ExactName, _name, _subtypeOf, [.. _annotatedWith, annotation]) { IsUnfilteredByName = IsUnfilteredByName };
    }

    internal ClassMatcher SubclassOf(string superType)
    {
        return new ClassMatcher($"{Description} extending {superType}", ExactName, _name, [.. _subtypeOf, superType], _annotatedWith) { IsUnfilteredByName = IsUnfilteredByName };
    }

    /// <summary>Evaluates the name test, which is all that runs before a class header is read.</summary>
    internal bool MatchesName(string className)
    {
        return _name(className);
    }

    /// <summary>Evaluates the supertype tests for a class that was loaded before its header could be read, using what the hierarchy knows about it.</summary>
    internal bool MatchesHierarchy(string className, ClassHierarchy hierarchy)
    {
        return _subtypeOf.All(superType => hierarchy.IsSubtypeOf(className, superType));
    }

    /// <summary>Evaluates every test against a class header.</summary>
    internal bool Matches(ClassHeader header, ClassHierarchy hierarchy)
    {
        if (!_name(header.Name))
            return false;

        foreach (string superType in _subtypeOf)
        {
            if (!hierarchy.IsSubtype(header.SuperName, header.Interfaces, superType))
                return false;
        }

        foreach (string annotation in _annotatedWith)
        {
            if (!header.Annotations.Contains(annotation))
                return false;
        }

        return true;
    }
}

using JvmBridge.Agents;

using Xunit;

namespace JvmBridge.Tests.Agents;

/// <summary>Verifies the immutable set of registered patches that the class-file hook reads.</summary>
public sealed class PatchSetTests
{
    /// <summary>Patches that name a class and patches that match classes come back in the order they were registered.</summary>
    [Fact]
    public void CandidatesKeepRegistrationOrder()
    {
        ClassPatch first = ClassPatch.For(className: "a/B");
        ClassPatch package = ClassPatch.ForPackage(package: "a");
        ClassPatch second = ClassPatch.For(className: "a/B");
        PatchSet set = PatchSet.Empty.With(first, sequence: 1).With(package, sequence: 2).With(second, sequence: 3);

        Assert.Equal(expected: [first, package, second], set.Candidates(className: "a/B"));
        Assert.Equal(expected: [package], set.Candidates(className: "a/C"));
        Assert.Empty(set.Candidates(className: "x/Y"));
    }

    /// <summary>Adding or removing a patch builds a new set and leaves the one a class load is reading alone.</summary>
    [Fact]
    public void ChangesBuildNewSets()
    {
        ClassPatch keep = ClassPatch.For(className: "a/B");
        ClassPatch drop = ClassPatch.For(className: "a/C");
        PatchSet both = PatchSet.Empty.With(keep, sequence: 1).With(drop, sequence: 2);
        PatchSet without = both.Without(drop);

        // The set a patch was removed from still has it, and only the new set lacks it.
        Assert.Equal(expected: 2, both.Count);
        Assert.Equal(expected: 1, without.Count);
        Assert.True(both.Contains(drop));
        Assert.False(without.Contains(drop));

        // The shared empty set was never written to.
        Assert.Equal(expected: 0, PatchSet.Empty.Count);
    }

    /// <summary>The set says whether any patch needs supertypes or annotations, so the runtime only learns them when something asks.</summary>
    [Fact]
    public void ReportsWhatTheClassHookMustLearn()
    {
        PatchSet names = PatchSet.Empty.With(ClassPatch.For(className: "a/B"), sequence: 1);
        PatchSet hierarchy = names.With(ClassPatch.ForSubclassesOf(superType: "a/Base"), sequence: 2);
        PatchSet annotations = hierarchy.With(ClassPatch.ForAnnotatedWith(annotationType: "a/Marker"), sequence: 3);

        // Patches that name their class need nothing beyond the name.
        Assert.False(names.NeedsHierarchy);
        Assert.False(names.NeedsAnnotations);

        // Each need appears with the first patch that has it, and stays when further patches are added.
        Assert.True(hierarchy.NeedsHierarchy);
        Assert.False(hierarchy.NeedsAnnotations);
        Assert.True(annotations.NeedsAnnotations);
    }
}

using JvmBridge.Agents;
using JvmBridge.ClassFiles;
using JvmBridge.Tests.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.Agents;

/// <summary>Verifies which classes a patch targets: by name, package, supertype and class-level annotation.</summary>
public sealed class ClassMatcherTests
{
    private const string Deprecated = "java/lang/Deprecated";

    private static byte[] Annotated => ClassFileFixtures.Read(resource: "ClassFiles.Annotated.class");

    private static ClassHierarchy Collections
    {
        get
        {
            ClassHierarchy hierarchy = new();
            hierarchy.Add(className: "java/util/AbstractList", superName: "java/util/AbstractCollection", ["java/util/List"]);
            hierarchy.Add(className: "java/util/AbstractCollection", superName: "java/lang/Object", ["java/util/Collection"]);

            return hierarchy;
        }
    }

    /// <summary>A class-level annotation is found in the class bytes whether it is visible at run time or only recorded in the class file.</summary>
    [Fact]
    public void AnnotationTestsReadTheClassBytes()
    {
        Assert.True(ClassPatch.ForAnnotatedWith(Deprecated).Matches(Annotated));
        Assert.True(ClassPatch.ForAnnotatedWith(annotationType: "fixture/Annotated$Hidden").Matches(Annotated));
        Assert.False(ClassPatch.ForAnnotatedWith(annotationType: "fixture/Missing").Matches(Annotated));
        Assert.False(ClassPatch.ForAnnotatedWith(Deprecated).Matches(ClassFileFixtures.Sample));
    }

    /// <summary>Narrowing a patch keeps its other tests, so every one of them must hold.</summary>
    [Fact]
    public void CombinesTheNameTestWithSupertypeAndAnnotationTests()
    {
        ClassPatch annotatedInPackage = ClassPatch.ForPackage(package: "fixture").WhereAnnotatedWith(Deprecated);
        ClassPatch runnableInPackage = ClassPatch.ForPackage(package: "fixture").WhereSubclassOf(superType: "java/lang/Runnable");
        ClassPatch wrongPackage = ClassPatch.ForPackage(package: "other").WhereAnnotatedWith(Deprecated);

        Assert.True(annotatedInPackage.Matches(Annotated));
        Assert.False(annotatedInPackage.Matches(ClassFileFixtures.Sample));
        Assert.True(runnableInPackage.Matches(Annotated));
        Assert.False(runnableInPackage.Matches(ClassFileFixtures.Sample));
        Assert.False(wrongPackage.Matches(Annotated));
    }

    /// <summary>A patch that names its class targets that class only.</summary>
    [Fact]
    public void NamedPatchesTargetTheirClass()
    {
        ClassPatch patch = ClassPatch.For(className: "fixture/Sample");

        Assert.True(patch.Matches(ClassFileFixtures.Sample));
        Assert.False(patch.Matches(Annotated));
    }

    /// <summary>A package test accepts the classes directly in the package, nested packages only when asked, and never a package that merely shares the prefix.</summary>
    [Fact]
    public void PackageTestsRespectNestedPackages()
    {
        byte[] direct = new ClassFileWriter(name: "fixture/Direct").ToArray();
        byte[] deep = new ClassFileWriter(name: "fixture/inner/Deep").ToArray();
        byte[] neighbour = new ClassFileWriter(name: "fixtureother/Class").ToArray();
        ClassPatch flat = ClassPatch.ForPackage(package: "fixture");
        ClassPatch recursive = ClassPatch.ForPackage(package: "fixture/", includeSubpackages: true);

        Assert.True(flat.Matches(direct));
        Assert.False(flat.Matches(deep));
        Assert.True(recursive.Matches(direct));
        Assert.True(recursive.Matches(deep));
        Assert.False(flat.Matches(neighbour));
        Assert.False(recursive.Matches(neighbour));
    }

    /// <summary>A name filter sees the internal class name.</summary>
    [Fact]
    public void PredicateTestsSeeTheInternalName()
    {
        ClassPatch endsWithSample = ClassPatch.ForMatching(static name => name.EndsWith(value: "/Sample", StringComparison.Ordinal), description: "sample classes");

        Assert.True(endsWithSample.Matches(ClassFileFixtures.Sample));
        Assert.False(endsWithSample.Matches(Annotated));
        Assert.Equal(expected: "sample classes", endsWithSample.ClassName);
    }

    /// <summary>Dotted names are rejected up front, as For does.</summary>
    [Fact]
    public void RejectsDottedNames()
    {
        ArgumentException package = Assert.Throws<ArgumentException>(static () => ClassPatch.ForPackage(package: "com.acme"));
        ArgumentException superType = Assert.Throws<ArgumentException>(static () => ClassPatch.ForSubclassesOf(superType: "java.lang.Runnable"));
        ArgumentException annotation = Assert.Throws<ArgumentException>(static () => ClassPatch.ForAnnotatedWith(annotationType: "java.lang.Deprecated"));

        Assert.Equal(expected: "package", package.ParamName);
        Assert.Equal(expected: "superType", superType.ParamName);
        Assert.Equal(expected: "annotationType", annotation.ParamName);
    }

    /// <summary>The result names the class that was patched, not the description of what the patch matches.</summary>
    [Fact]
    public void ResultsNameThePatchedClass()
    {
        ClassPatch patch = ClassPatch.ForPackage(package: "fixture").ReplaceConstant(oldValue: "hello", newValue: "goodbye");

        ClassPatchResult result = patch.Apply(ClassFileFixtures.Sample);

        Assert.Equal(expected: "package fixture", patch.ClassName);
        Assert.True(result.Changed);
        Assert.Equal(expected: "fixture/Sample", result.ClassName);
        Assert.StartsWith(expectedStartString: "patched fixture/Sample", result.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A class that does not pass the name test is left alone, with a reason that names the matcher.</summary>
    [Fact]
    public void SkipsClassesTheNameTestRejects()
    {
        ClassPatchResult result = ClassPatch.ForPackage(package: "other").ReplaceConstant(oldValue: "hello", newValue: "goodbye").Apply(ClassFileFixtures.Sample);

        Assert.False(result.Changed);
        Assert.Contains(expectedSubstring: "package other", result.SkippedReason, StringComparison.Ordinal);
    }

    /// <summary>A subtype test follows the recorded hierarchy through other types, and a direct interface needs no record.</summary>
    [Fact]
    public void SubtypeTestsWalkTheKnownHierarchy()
    {
        ClassPatch runnable = ClassPatch.ForSubclassesOf(superType: "java/lang/Runnable");
        ClassPatch collection = ClassPatch.ForSubclassesOf(superType: "java/util/Collection");
        ClassPatch itself = ClassPatch.ForSubclassesOf(superType: "fixture/Annotated");

        Assert.True(runnable.Matches(Annotated));
        Assert.True(collection.Matches(Annotated, Collections));
        Assert.False(itself.Matches(Annotated, Collections));
        Assert.False(runnable.Matches(ClassFileFixtures.Sample));
    }

    /// <summary>A supertype that was never recorded ends the walk, so a class whose ancestry runs through it does not match.</summary>
    [Fact]
    public void UnknownAncestorsDoNotMatch()
    {
        ClassPatch collection = ClassPatch.ForSubclassesOf(superType: "java/util/Collection");
        ClassHierarchy partial = new();
        partial.Add(className: "java/util/AbstractList", superName: "java/util/AbstractCollection", []);

        Assert.False(collection.Matches(Annotated));
        Assert.False(collection.Matches(Annotated, partial));
        Assert.True(collection.Matches(Annotated, Collections));
        Assert.True(Collections.IsSubtypeOf(className: "java/util/AbstractList", ancestor: "java/util/Collection"));
        Assert.False(Collections.IsSubtypeOf(className: "java/util/AbstractList", ancestor: "java/util/AbstractList"));
    }
}

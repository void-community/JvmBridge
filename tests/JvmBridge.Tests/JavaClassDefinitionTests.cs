using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>Verifies helper class definitions.</summary>
public sealed class JavaClassDefinitionTests
{
    /// <summary>Nested classes are defined after the classes they extend, whatever order they were declared in.</summary>
    [Fact]
    public void OrdersSuperclassesBeforeSubclasses()
    {
        JavaClassDefinition baseClass = Define(name: "pkg/Base", superName: "java/lang/Object");
        JavaClassDefinition middle = Define(name: "pkg/Middle", superName: "pkg/Base");
        JavaClassDefinition leaf = Define(name: "pkg/Leaf", superName: "pkg/Middle");

        JavaClassDefinition outer = new(
            name: "pkg/Outer",
            new ClassFileWriter(name: "pkg/Outer", superName: "pkg/Leaf").AddNativeMethod(name: "go", descriptor: "()V").ToArray(),
            [leaf, baseClass, middle]
        );

        string[] order = [.. outer.GetDefinitionOrder().Select(static definition => definition.Name)];

        Assert.Equal(["pkg/Base", "pkg/Middle", "pkg/Leaf", "pkg/Outer"], order);
    }

    /// <summary>A definition checks that its bytes describe the class it names.</summary>
    [Fact]
    public void RejectsBytesThatDescribeAnotherClass()
    {
        byte[] bytes = new ClassFileWriter(name: "pkg/Helper").AddNativeMethod(name: "go", descriptor: "()V").ToArray();

        ArgumentException mismatch = Assert.Throws<ArgumentException>(() => new JavaClassDefinition(name: "pkg/Other", bytes));
        ArgumentException invalid = Assert.Throws<ArgumentException>(() => new JavaClassDefinition(name: "pkg/Helper", [1, 2, 3]));
        JavaClassDefinition named = JavaClassDefinition.FromBytes(bytes);

        Assert.Contains(expectedSubstring: "pkg/Helper", mismatch.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "not a valid class file", invalid.Message, StringComparison.Ordinal);
        Assert.Equal(expected: "pkg/Helper", named.Name);
        Assert.Equal(bytes, named.Bytes.ToArray());
    }

    /// <summary>Classes that extend each other cannot be ordered, and say so.</summary>
    [Fact]
    public void ReportsDependencyCycles()
    {
        JavaClassDefinition first = Define(name: "pkg/A", superName: "pkg/B");
        JavaClassDefinition second = Define(name: "pkg/B", superName: "pkg/A");

        JavaClassDefinition outer = new(
            name: "pkg/Outer",
            new ClassFileWriter(name: "pkg/Outer").AddNativeMethod(name: "go", descriptor: "()V").ToArray(),
            [first, second]
        );

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(outer.GetDefinitionOrder);

        Assert.Contains(expectedSubstring: "cycle", failure.Message, StringComparison.Ordinal);
    }

    private static JavaClassDefinition Define(string name, string superName)
    {
        return new(name, new ClassFileWriter(name, superName).AddNativeMethod(name: "go", descriptor: "()V").ToArray());
    }
}

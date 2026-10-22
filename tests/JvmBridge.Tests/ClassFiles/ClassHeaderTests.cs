using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies the lightweight class-file header reader that class matchers use.</summary>
public sealed class ClassHeaderTests
{
    /// <summary>The header agrees with the full class-file model.</summary>
    [Fact]
    public void AgreesWithTheFullModel()
    {
        byte[] bytes = ClassFileFixtures.Sample;
        JavaClassFile model = JavaClassFile.Read(bytes);

        ClassHeader header = ClassHeader.Read(bytes, includeAnnotations: true);

        Assert.Equal(model.Name, header.Name);
        Assert.Equal(model.SuperName, header.SuperName);
        Assert.Equal(model.Interfaces, header.Interfaces);
        Assert.Empty(header.Annotations);
    }

    /// <summary>Long and double constants take two constant-pool slots, which the walk over the pool must account for.</summary>
    [Fact]
    public void HandlesTwoSlotConstants()
    {
        ClassFileWriter writer = new(name: "pkg/Wide", superName: "java/util/ArrayList");
        int first = writer.AddLong(1L << 40);
        int second = writer.AddDouble(value: 2.5);
        int third = writer.AddString(value: "after");

        ClassHeader header = ClassHeader.Read(writer.ToArray(), includeAnnotations: true);

        Assert.Equal(first + 2, second);
        Assert.True(third > second + 2);
        Assert.Equal(expected: "pkg/Wide", header.Name);
        Assert.Equal(expected: "java/util/ArrayList", header.SuperName);
    }

    /// <summary>The class-level annotations of a real javac class come from both attributes, and skipping their element values (strings, arrays, classes, enums and nested annotations) stays in step.</summary>
    [Fact]
    public void ReadsVisibleAndInvisibleAnnotations()
    {
        ClassHeader header = ClassHeader.Read(ClassFileFixtures.Read(resource: "ClassFiles.Annotated.class"), includeAnnotations: true);

        Assert.Equal(expected: "fixture/Annotated", header.Name);
        Assert.Equal(expected: "java/util/AbstractList", header.SuperName);
        Assert.Equal(["java/io/Serializable", "java/lang/Runnable"], header.Interfaces);
        Assert.Equal(["java/lang/Deprecated", "fixture/Annotated$Visible", "fixture/Annotated$Hidden"], header.Annotations);
    }

    /// <summary>Annotations are only read on request, so the common case does not walk the members.</summary>
    [Fact]
    public void SkipsAnnotationsUnlessRequested()
    {
        ClassHeader header = ClassHeader.Read(ClassFileFixtures.Read(resource: "ClassFiles.Annotated.class"), includeAnnotations: false);

        Assert.Equal(expected: "fixture/Annotated", header.Name);
        Assert.Empty(header.Annotations);
    }

    /// <summary>Malformed bytes report failure through TryRead instead of throwing in a JVM callback.</summary>
    [Fact]
    public void TryReadRejectsMalformedBytes()
    {
        byte[] valid = ClassFileFixtures.Sample;
        byte[] wrongMagic = [.. valid];
        wrongMagic[0] = 0;

        Assert.False(ClassHeader.TryRead(wrongMagic, includeAnnotations: true, out ClassHeader? first));
        Assert.Null(first);
        Assert.False(ClassHeader.TryRead(valid.AsSpan(start: 0, length: 40), includeAnnotations: false, out _));
        Assert.False(ClassHeader.TryRead([], includeAnnotations: false, out _));
        Assert.True(ClassHeader.TryRead(valid, includeAnnotations: true, out ClassHeader? header));
        Assert.Equal(expected: "fixture/Sample", header?.Name);
        ClassFormatException failure = Assert.Throws<ClassFormatException>(() => ClassHeader.Read(wrongMagic, includeAnnotations: false));

        Assert.Contains(expectedSubstring: "magic", failure.Message, StringComparison.Ordinal);
    }
}

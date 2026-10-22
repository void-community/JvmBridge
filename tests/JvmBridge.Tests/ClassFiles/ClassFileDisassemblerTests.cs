using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies the javap-style text of class files.</summary>
public sealed class ClassFileDisassemblerTests
{
    /// <summary>Immediate operands, local variable indexes and array types are decoded.</summary>
    [Fact]
    public void DecodesImmediateOperands()
    {
        byte[] code = [0x03, 0x3c, 0x84, 0x01, 0x05, 0x10, 0xfb, 0x11, 0x01, 0x00, 0x57, 0xbc, 0x0a, 0xb1];

        string text = ClassFileDisassembler.Disassemble(new ClassFileWriter(name: "pkg/Mixed").AddMethod(name: "run", descriptor: "()V", maxStack: 2, maxLocals: 2, code).ToArray());

        Assert.Contains(expectedSubstring: "iconst_0", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "istore_1", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "iinc 1, 5", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "bipush -5", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "sipush 256", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "newarray int", text, StringComparison.Ordinal);
    }

    /// <summary>Instructions show their opcode names and the constants they reference, so a call and a string can be read at a glance.</summary>
    [Fact]
    public void DecodesInstructionsAndConstants()
    {
        string length = ClassFileDisassembler.Disassemble(ClassFileFixtures.Sample, method: "length");
        string greeting = ClassFileDisassembler.Disassemble(ClassFileFixtures.Sample, method: "greeting");

        Assert.Contains(expectedSubstring: "0: aload_0", length, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "// Method java/lang/String.length:()I", length, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "ireturn", length, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "// String \"hello\"", greeting, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "areturn", greeting, StringComparison.Ordinal);
    }

    /// <summary>A method filter shows only the matching methods and no class header, by name or by name and descriptor.</summary>
    [Fact]
    public void FiltersMethods()
    {
        string byName = ClassFileDisassembler.Disassemble(ClassFileFixtures.Sample, method: "greeting");
        string byDescriptor = ClassFileDisassembler.Disassemble(ClassFileFixtures.Sample, method: "greeting()Ljava/lang/String;");
        string missing = ClassFileDisassembler.Disassemble(ClassFileFixtures.Sample, method: "absent");

        Assert.Equal(byName, byDescriptor);
        Assert.DoesNotContain(expectedSubstring: "class fixture/Sample", byName, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "method public static length", byName, StringComparison.Ordinal);
        Assert.Equal(expected: "\n", missing);
    }

    /// <summary>The text is the same each time, which is what makes it usable for snapshots.</summary>
    [Fact]
    public void IsDeterministic()
    {
        Assert.Equal(ClassFileDisassembler.Disassemble(ClassFileFixtures.Sample), ClassFileDisassembler.Disassemble(ClassFileFixtures.Sample));
    }

    /// <summary>A forward branch shows its absolute target and a switch lists its cases.</summary>
    [Fact]
    public void ShowsBranchTargetsAndSwitchCases()
    {
        byte[] branch = [0x1a, 0x99, 0x00, 0x05, 0x04, 0xac, 0x03, 0xac];

        byte[] table =
        [
            0x1a, 0xaa, 0x00, 0x00, 0x00, 0x00, 0x00, 0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x17, 0x00, 0x00, 0x00, 0x19, 0x04, 0xac, 0x05, 0xac
        ];

        string text = ClassFileDisassembler.Disassemble(
            new ClassFileWriter(name: "pkg/Flow").AddMethod(name: "pick", descriptor: "(I)I", maxStack: 1, maxLocals: 1, branch).AddMethod(name: "route", descriptor: "(I)I", maxStack: 1, maxLocals: 1, table).ToArray()
        );

        Assert.Contains(expectedSubstring: "1: ifeq 6", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "6: iconst_0", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "1: tableswitch { 0: 24, 1: 26, default: 26 }", text, StringComparison.Ordinal);
    }

    /// <summary>The class header lists the supertype, the version and the flags, and each field and method follows with its flags.</summary>
    [Fact]
    public void ShowsTheHeaderFieldsAndMethodFlags()
    {
        string text = ClassFileDisassembler.Disassemble(ClassFileFixtures.Sample);

        Assert.Contains(expectedSubstring: "class fixture/Sample extends java/lang/Object", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "version 52.0, flags public", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "field public static final I LIMIT", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "field private I count", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "method public static length(Ljava/lang/String;)I", text, StringComparison.Ordinal);
    }
}

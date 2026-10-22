using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies the read-only class-file model against a javac-compiled class.</summary>
public sealed class ClassFileReaderTests
{
    private const string SortDescriptor = "(Ljava/util/List;Ljava/util/Comparator;)V";

    /// <summary>Instructions tile the code exactly, including switch padding.</summary>
    [Fact]
    public void DecodesEveryInstructionIncludingSwitches()
    {
        JavaClassFile file = JavaClassFile.Read(ClassFileFixtures.Sample);

        foreach (CodeAttribute code in file.Methods.Select(static method => method.Code).OfType<CodeAttribute>())
        {
            int expected = 0;

            foreach (Instruction instruction in code.Instructions)
            {
                Assert.Equal(expected, instruction.Offset);
                expected += instruction.Length;
            }

            Assert.Equal(code.CodeLength, expected);
        }

        byte[] opcodes = [.. file.Methods.SelectMany(static method => method.Code?.Instructions ?? []).Select(static instruction => instruction.Opcode)];
        Assert.Contains(Opcodes.TableSwitch, opcodes);
        Assert.Contains(Opcodes.LookupSwitch, opcodes);
    }

    /// <summary>Class files store modified UTF-8: NUL is two bytes and a surrogate pair is two three-byte sequences.</summary>
    [Fact]
    public void DecodesModifiedUtf8Constants()
    {
        JavaClassFile file = JavaClassFile.Read(ClassFileFixtures.Sample);

        Assert.Contains(expected: "héllo ☃ 😀 \u0000 end", file.GetStrings());
    }

    /// <summary>The wide prefix widens iinc to six bytes and other instructions to four.</summary>
    [Fact]
    public void DecodesWideInstructions()
    {
        byte[] code = [Opcodes.Wide, Opcodes.Iinc, 0x01, 0x00, 0x00, 0x02, Opcodes.Wide, 0x15, 0x01, 0x00, 0xb1];
        byte[] bytes = new ClassFileWriter(name: "pkg/Wide").AddMethod(name: "run", descriptor: "()V", maxStack: 1, maxLocals: 1, code).ToArray();

        int[] lengths = [.. JavaClassFile.Read(bytes).GetCode(name: "run").Instructions.Select(static instruction => instruction.Length)];

        Assert.Equal([6, 4, 1], lengths);
    }

    /// <summary>Typed constants are exposed, and a Long or Double hides the slot after it.</summary>
    [Fact]
    public void ReadsTypedConstantsAndTwoSlotConstants()
    {
        JavaClassFile file = JavaClassFile.Read(ClassFileFixtures.Sample);
        LongEntry big = Assert.Single(file.ConstantPool.OfType<LongEntry>());
        DoubleEntry ratio = Assert.Single(file.ConstantPool.OfType<DoubleEntry>());
        FloatEntry scale = Assert.Single(file.ConstantPool.OfType<FloatEntry>());

        Assert.Equal(expected: 5000000000L, big.Value);
        Assert.Equal(expected: 3.5, ratio.Value);
        Assert.Equal(expected: 2.5f, scale.Value);
        Assert.Equal(expected: 1, file.CountOf<IntegerEntry>(static entry => entry.Value == 100000));
        Assert.Null(file.ConstantPool[index: 0]);
        Assert.Null(file.ConstantPool[big.Index + 1]);
        Assert.Null(file.ConstantPool[ratio.Index + 1]);
        Assert.Contains(expected: "hello", file.GetStrings());
    }

    /// <summary>The header, hierarchy and member tables are exposed.</summary>
    [Fact]
    public void ReadsVersionHierarchyAndMembers()
    {
        JavaClassFile file = JavaClassFile.Read(ClassFileFixtures.Sample);
        ClassMember sort = file.GetMethod(name: "sortTwice");
        ClassMember limit = file.GetField(name: "LIMIT");
        ClassMember count = file.GetField(name: "count");

        Assert.Equal(expected: 52, file.MajorVersion);
        Assert.Equal(expected: "fixture/Sample", file.Name);
        Assert.Equal(expected: "java/lang/Object", file.SuperName);
        Assert.Empty(file.Interfaces);
        Assert.Equal(SortDescriptor, sort.Descriptor);
        Assert.Equal(expected: 0x9, sort.AccessFlags);
        Assert.Equal(expected: "I", limit.Descriptor);
        Assert.Equal(expected: 0x19, limit.AccessFlags);
        Assert.Equal(expected: 0x2, count.AccessFlags);
        Assert.Null(count.Code);
    }

    /// <summary>Input without the magic number, or with bytes after the class, is rejected.</summary>
    [Fact]
    public void RejectsBadMagicAndTrailingBytes()
    {
        byte[] magic = ClassFileFixtures.Sample;
        magic[0] = 0;
        byte[] trailing = [.. ClassFileFixtures.Sample, 0];

        ClassFormatException badMagic = Assert.Throws<ClassFormatException>(() => JavaClassFile.Read(magic));
        ClassFormatException extra = Assert.Throws<ClassFormatException>(() => JavaClassFile.Read(trailing));

        Assert.Contains(expectedSubstring: "magic", badMagic.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "trailing", extra.Message, StringComparison.Ordinal);
    }

    /// <summary>A constant index that names the wrong kind of entry is rejected.</summary>
    [Fact]
    public void RejectsConstantIndicesOfTheWrongKind()
    {
        byte[] bytes = ClassFileFixtures.Sample;
        JavaClassFile file = JavaClassFile.Read(bytes);
        int thisClass = file.AccessFlagsOffset + 2;
        int utf8 = file.ConstantPool.OfType<Utf8Entry>().First().Index;
        bytes[thisClass] = (byte)(utf8 >> 8);
        bytes[thisClass + 1] = (byte)utf8;

        ClassFormatException failure = Assert.Throws<ClassFormatException>(() => JavaClassFile.Read(bytes));

        Assert.Contains(expectedSubstring: "expected ClassEntry", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A constant index outside the pool is rejected.</summary>
    [Fact]
    public void RejectsConstantIndicesOutsideThePool()
    {
        byte[] bytes = ClassFileFixtures.Sample;
        int thisClass = JavaClassFile.Read(bytes).AccessFlagsOffset + 2;
        bytes[thisClass] = 0xff;
        bytes[thisClass + 1] = 0xff;

        ClassFormatException failure = Assert.Throws<ClassFormatException>(() => JavaClassFile.Read(bytes));

        Assert.Contains(expectedSubstring: "out of range", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Every strict prefix of a class file is rejected with a clear exception rather than an out-of-range access.</summary>
    [Fact]
    public void RejectsEveryTruncatedPrefix()
    {
        byte[] bytes = ClassFileFixtures.Sample;

        for (int length = 0; length < bytes.Length; length++)
        {
            ClassFormatException failure = Assert.Throws<ClassFormatException>(() => JavaClassFile.Read(bytes.AsSpan(start: 0, length)));

            Assert.False(string.IsNullOrWhiteSpace(failure.Message));
        }
    }

    /// <summary>A Utf8 constant with an invalid byte is rejected and says where.</summary>
    [Fact]
    public void RejectsMalformedUtf8Constants()
    {
        byte[] bytes = ClassFileFixtures.Sample;
        Utf8Entry entry = JavaClassFile.Read(bytes).ConstantPool.OfType<Utf8Entry>().First(candidate => candidate.Value == "hello");
        bytes[entry.Offset + 3] = 0xff;

        ClassFormatException failure = Assert.Throws<ClassFormatException>(() => JavaClassFile.Read(bytes));

        Assert.Contains($"offset {entry.Offset}", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A tableswitch whose high bound is below its low bound is rejected.</summary>
    [Fact]
    public void RejectsTableswitchWithInvertedBounds()
    {
        byte[] code = [Opcodes.TableSwitch, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0xb1];
        byte[] bytes = new ClassFileWriter(name: "pkg/Switch").AddMethod(name: "run", descriptor: "()V", maxStack: 1, maxLocals: 1, code).ToArray();

        ClassFormatException failure = Assert.Throws<ClassFormatException>(() => JavaClassFile.Read(bytes));

        Assert.Contains(expectedSubstring: "tableswitch", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An unknown constant tag names the tag and offset.</summary>
    [Fact]
    public void RejectsUnknownConstantTags()
    {
        byte[] bytes = ClassFileFixtures.Sample;
        bytes[10] = 2;

        ClassFormatException failure = Assert.Throws<ClassFormatException>(() => JavaClassFile.Read(bytes));

        Assert.Contains(expectedSubstring: "tag 2 at offset 10", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A reserved opcode in code is rejected with its offset.</summary>
    [Fact]
    public void RejectsUnknownOpcodes()
    {
        byte[] bytes = ClassFileFixtures.Sample;
        bytes[JavaClassFile.Read(bytes).GetCode(name: "greeting").CodeOffset] = 0xfe;

        ClassFormatException failure = Assert.Throws<ClassFormatException>(() => JavaClassFile.Read(bytes));

        Assert.Contains(expectedSubstring: "0xfe", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Only instructions that have a wide form may follow the wide prefix.</summary>
    [Fact]
    public void RejectsWideBeforeAnInstructionWithoutAWideForm()
    {
        byte[] code = [Opcodes.Wide, Opcodes.Nop, 0, 0, 0xb1];
        byte[] bytes = new ClassFileWriter(name: "pkg/Wide").AddMethod(name: "run", descriptor: "()V", maxStack: 1, maxLocals: 1, code).ToArray();

        ClassFormatException failure = Assert.Throws<ClassFormatException>(() => JavaClassFile.Read(bytes));

        Assert.Contains(expectedSubstring: "wide", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Invocation operands resolve to owner, name and descriptor.</summary>
    [Fact]
    public void ResolvesInvocationOperands()
    {
        JavaClassFile file = JavaClassFile.Read(ClassFileFixtures.Sample);
        Instruction[] calls = [.. file.GetCode(name: "sortTwice").Instructions.Where(instruction => instruction.Opcode == Opcodes.InvokeStatic)];
        Instruction size = file.GetCode(name: "size").Instructions.Single(instruction => instruction.Opcode == Opcodes.InvokeInterface);
        MemberReference expected = new(ConstantTag.Methodref, Owner: "java/util/Collections", Name: "sort", SortDescriptor);

        Assert.Equal(expected: 2, calls.Length);
        Assert.All(calls, call => Assert.Equal(expected, file.GetMemberReference(call.ConstantIndex ?? 0)));
        Assert.Equal(expected: 5, size.Length);
        Assert.Equal(
            new MemberReference(ConstantTag.InterfaceMethodref, Owner: "java/util/List", Name: "size", Descriptor: "()I"),
            file.GetMemberReference(size.ConstantIndex ?? 0)
        );
    }
}

using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>Verifies that edits keep every constant index and code offset, and change only what they claim to.</summary>
public sealed class ClassFileEditorTests
{
    private const string SortDescriptor = "(Ljava/util/List;Ljava/util/Comparator;)V";
    private static readonly JavaMethod CollectionsSort = JavaMethod.Static(owner: "java/util/Collections", name: "sort", SortDescriptor);

    /// <summary>An add that needs two slots when one is left fails without changing the class.</summary>
    [Fact]
    public void AFailedAddLeavesTheClassUnchanged()
    {
        ClassFileWriter writer = new(name: "pkg/Full");

        for (int value = 0; value < 65529; value++)
            Assert.True(writer.AddInteger(value) > 0);

        byte[] full = writer.ToArray();
        ClassFileEditor editor = new(full);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Assert.True(editor.AddClass(name: "new/Cls") > 0));

        Assert.Contains(expectedSubstring: "full", failure.Message, StringComparison.Ordinal);
        Assert.Equal(full, editor.ToArray());
        Assert.True(editor.AddUtf8(value: "fits") > 0);
    }

    /// <summary>Each constant is added once, and an existing constant is reused without growing the file.</summary>
    [Fact]
    public void AddsEachConstantOnlyOnce()
    {
        byte[] original = ClassFileFixtures.Sample;
        ClassFileEditor editor = new(original);
        int existing = JavaClassFile.Read(original).ConstantPool.OfType<Utf8Entry>().Single(entry => entry.Value == "sortTwice").Index;

        Assert.Equal(existing, editor.AddUtf8(value: "sortTwice"));
        Assert.Equal(original, editor.ToArray());

        int method = editor.AddMethodReference(owner: "fixture/Helper", name: "sort", SortDescriptor);
        int length = editor.ToArray().Length;

        Assert.Equal(method, editor.AddMethodReference(owner: "fixture/Helper", name: "sort", SortDescriptor));
        Assert.Equal(length, editor.ToArray().Length);
        Assert.Equal(expected: 1, editor.Model.CountOf<ClassEntry>(entry => editor.Model.GetClassName(entry.Index) == "fixture/Helper"));
    }

    /// <summary>An explicit descriptor overrides the derived one.</summary>
    [Fact]
    public void AnExplicitReplacementDescriptorIsUsed()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);

        int replaced = editor.ReplaceInvocation(
            JavaMethod.Virtual(owner: "java/lang/String", name: "length", descriptor: "()I"),
            toOwner: "fixture/Helper",
            toName: "touch",
            toDescriptor: "(Ljava/lang/Object;)V"
        );

        Assert.Equal(expected: 1, replaced);
        MemberReference call = Assert.Single(StaticCalls(editor, method: "length"));
        Assert.Equal(new MemberReference(ConstantTag.Methodref, Owner: "fixture/Helper", Name: "touch", Descriptor: "(Ljava/lang/Object;)V"), call);
    }

    /// <summary>Appending constants leaves every existing entry, instruction and byte of code where it was.</summary>
    [Fact]
    public void AppendingConstantsKeepsIndicesAndCodeOffsets()
    {
        byte[] original = ClassFileFixtures.Sample;
        JavaClassFile before = JavaClassFile.Read(original);
        ClassFileEditor editor = new(original);

        int method = editor.AddMethodReference(owner: "fixture/Helper", name: "sort", SortDescriptor);
        int wide = editor.AddLong(value: 42);
        int text = editor.AddString(value: "new");
        byte[] edited = editor.ToArray();
        JavaClassFile after = JavaClassFile.Read(edited);

        Assert.True(method < wide && wide < text);

        for (int index = 1; index < before.ConstantPool.Count; index++)
            Assert.Equal(before.ConstantPool[index], after.ConstantPool[index]);

        foreach (ClassMember member in before.Methods.Where(static candidate => candidate.Code is not null))
        {
            CodeAttribute oldCode = before.GetCode(member.Name);
            CodeAttribute newCode = after.GetCode(member.Name);

            Assert.Equal(oldCode.Instructions, newCode.Instructions);
            Assert.Equal(
                original.AsSpan(oldCode.CodeOffset, oldCode.CodeLength).ToArray(),
                edited.AsSpan(newCode.CodeOffset, newCode.CodeLength).ToArray()
            );
        }
    }

    /// <summary>Class, field and method access flags can be replaced.</summary>
    [Fact]
    public void ChangesAccessFlags()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);

        editor.SetAccess(accessFlags: 0x0001);
        int fields = editor.SetFieldAccess(name: "count", descriptor: null, accessFlags: 0x0001);
        int methods = editor.SetMethodAccess(name: "count", descriptor: "()I", accessFlags: 0x0011);
        int missing = editor.SetMethodAccess(name: "count", descriptor: "()V", accessFlags: 0x0001);

        Assert.Equal(expected: 1, fields);
        Assert.Equal(expected: 1, methods);
        Assert.Equal(expected: 0, missing);
        Assert.Equal(expected: 0x0001, editor.Model.AccessFlags);
        Assert.Equal(expected: 0x0001, editor.Model.GetField(name: "count").AccessFlags);
        Assert.Equal(expected: 0x0011, editor.Model.GetMethod(name: "count").AccessFlags);
    }

    /// <summary>An interface call is padded with nop so the following code keeps its offsets.</summary>
    [Fact]
    public void InterfaceInvocationsArePaddedWithNop()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);
        int codeLength = editor.Model.GetCode(name: "size").CodeLength;

        int replaced = editor.ReplaceInvocation(JavaMethod.Interface(owner: "java/util/List", name: "size", descriptor: "()I"), toOwner: "fixture/Helper", toName: "listSize");

        Assert.Equal(expected: 1, replaced);
        CodeAttribute code = editor.Model.GetCode(name: "size");
        Instruction[] instructions = [.. code.Instructions];
        byte[] opcodes = [.. instructions.Skip(count: 1).Take(count: 3).Select(static instruction => instruction.Opcode)];
        Assert.Equal(codeLength, code.CodeLength);
        Assert.Equal([Opcodes.InvokeStatic, Opcodes.Nop, Opcodes.Nop], opcodes);
        Assert.Equal(
            new MemberReference(ConstantTag.Methodref, Owner: "fixture/Helper", Name: "listSize", Descriptor: "(Ljava/util/List;)I"),
            editor.Model.GetMemberReference(instructions[1].ConstantIndex ?? 0)
        );
    }

    /// <summary>Long and Double constants take two slots.</summary>
    [Fact]
    public void LongAndDoubleConstantsTakeTwoSlots()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);

        int wide = editor.AddLong(1L << 40);
        int narrow = editor.AddInteger(value: 7);
        int doubled = editor.AddDouble(value: 1.25);

        Assert.Equal(wide + 2, narrow);
        Assert.Equal(narrow + 1, doubled);
        Assert.Null(editor.Model.ConstantPool[wide + 1]);
        Assert.Equal(1L << 40, editor.Model.GetEntry<LongEntry>(wide).Value);
        Assert.Equal(expected: 1.25, editor.Model.GetEntry<DoubleEntry>(doubled).Value);
    }

    /// <summary>A call that is not there, or is called differently, leaves the class byte-for-byte unchanged.</summary>
    [Fact]
    public void MissingInvocationsChangeNothing()
    {
        byte[] original = ClassFileFixtures.Sample;
        ClassFileEditor editor = new(original);

        int wrongKind = editor.ReplaceInvocation(JavaMethod.Virtual(owner: "java/util/Collections", name: "sort", SortDescriptor), toOwner: "fixture/Helper");
        int wrongName = editor.ReplaceInvocation(JavaMethod.Static(owner: "java/util/Collections", name: "shuffle", descriptor: "(Ljava/util/List;)V"), toOwner: "fixture/Helper");
        int wrongMethod = editor.ReplaceInvocation(CollectionsSort, toOwner: "fixture/Helper", methodName: "greeting");

        Assert.Equal(expected: 0, wrongKind + wrongName + wrongMethod);
        Assert.Equal(original, editor.ToArray());
    }

    /// <summary>Access flags outside 16 bits are rejected instead of being truncated.</summary>
    [Fact]
    public void RejectsAccessFlagsOutsideSixteenBits()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);
        byte[] original = editor.ToArray();

        ArgumentOutOfRangeException negative = Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetAccess(accessFlags: -1));
        ArgumentOutOfRangeException tooLarge = Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetAccess(accessFlags: 0x10000));
        ArgumentOutOfRangeException method = Assert.Throws<ArgumentOutOfRangeException>(() => Assert.True(editor.SetMethodAccess(name: "length", descriptor: null, accessFlags: -2) > 0));

        Assert.Equal(expected: "value", negative.ParamName);
        Assert.Equal(expected: "value", tooLarge.ParamName);
        Assert.Equal(expected: "value", method.ParamName);
        Assert.Equal(original, editor.ToArray());
    }

    /// <summary>An instruction cannot be pointed at a constant of a kind it cannot reference.</summary>
    [Fact]
    public void RejectsConstantsOfTheWrongKindForAnInstruction()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);
        int utf8 = editor.AddUtf8(value: "text");
        int wide = editor.AddLong(value: 5);
        int concrete = editor.AddMethodReference(owner: "java/util/List", name: "size", descriptor: "()I");
        Instruction call = editor.Model.GetCode(name: "size").Instructions.Single(instruction => instruction.Opcode == Opcodes.InvokeInterface);

        ArgumentException text = Assert.Throws<ArgumentException>(() => editor.RewriteConstantOperand(methodName: "size", methodDescriptor: null, call.Offset, utf8));
        ArgumentException methodRef = Assert.Throws<ArgumentException>(() => editor.RewriteConstantOperand(methodName: "size", methodDescriptor: null, call.Offset, concrete));
        ArgumentException loadLong = Assert.Throws<ArgumentException>(() => editor.RewriteConstantOperand(methodName: "limit", methodDescriptor: null, instructionOffset: 0, wide));

        Assert.Contains(expectedSubstring: "Utf8", text.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "Methodref", methodRef.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "Long", loadLong.Message, StringComparison.Ordinal);
    }

    /// <summary>The editor rejects input that is not a class file.</summary>
    [Fact]
    public void RejectsMalformedInput()
    {
        ClassFormatException failure = Assert.Throws<ClassFormatException>(static () => new ClassFileEditor([0xca, 0xfe]));

        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
    }

    /// <summary>A one-byte ldc operand cannot address a high index, and a missing instruction is reported.</summary>
    [Fact]
    public void RejectsOperandsThatCannotBeWritten()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);
        int last = 0;

        for (int value = 0; value < 300; value++)
            last = editor.AddInteger(1000 + value);

        ArgumentOutOfRangeException tooHigh = Assert.Throws<ArgumentOutOfRangeException>(() => editor.RewriteConstantOperand(methodName: "limit", methodDescriptor: null, instructionOffset: 0, last));
        ClassFormatException noOperand = Assert.Throws<ClassFormatException>(() => editor.RewriteConstantOperand(methodName: "limit", methodDescriptor: null, instructionOffset: 1, constantIndex: 1));
        ClassFormatException noMethod = Assert.Throws<ClassFormatException>(() => editor.RewriteConstantOperand(methodName: "missing", methodDescriptor: null, instructionOffset: 0, constantIndex: 1));

        Assert.Equal(expected: "constantIndex", tooHigh.ParamName);
        Assert.Contains(expectedSubstring: "no instruction", noOperand.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "no method", noMethod.Message, StringComparison.Ordinal);
    }

    /// <summary>Code replacement is safe at the edges, and a replacement that is not valid code changes nothing.</summary>
    [Fact]
    public void ReplaceCodeIsSafeAtTheEdgesAndKeepsTheEditorUsable()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);
        byte[] original = editor.ToArray();

        bool overflowing = editor.ReplaceCode(methodName: "greeting", methodDescriptor: null, int.MaxValue, [0x12], [0x12]);
        bool negative = editor.ReplaceCode(methodName: "greeting", methodDescriptor: null, codeOffset: -1, [0x12], [0x12]);
        ClassFormatException invalid = Assert.Throws<ClassFormatException>(() => Assert.True(editor.ReplaceCode(methodName: "greeting", methodDescriptor: null, codeOffset: 0, [0x12], [0xff])));

        Assert.False(overflowing);
        Assert.False(negative);
        Assert.Contains(expectedSubstring: "does not leave valid code", invalid.Message, StringComparison.Ordinal);
        Assert.Equal(original, editor.ToArray());
        Assert.True(editor.AddUtf8(value: "still usable") > 0);
    }

    /// <summary>A replacement owned by an interface needs an interface method reference.</summary>
    [Fact]
    public void ReplacementsCanBeOwnedByAnInterface()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);

        int replaced = editor.ReplaceInvocation(
            JavaMethod.Virtual(owner: "java/lang/String", name: "length", descriptor: "()I"),
            toOwner: "fixture/IHelper",
            ownerIsInterface: true
        );

        Instruction call = editor.Model.GetCode(name: "length").Instructions.Single(static instruction => instruction.Opcode == Opcodes.InvokeStatic);

        Assert.Equal(expected: 1, replaced);
        Assert.Equal(ConstantTag.InterfaceMethodref, editor.Model.GetMemberReference(call.ConstantIndex ?? 0).Kind);
    }

    /// <summary>Code is replaced only when the bytes are what the caller expects, and never at a different length.</summary>
    [Fact]
    public void ReplacesCodeOnlyWhenItMatches()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);
        int codeLength = editor.Model.GetCode(name: "length").CodeLength;
        byte[] original = editor.ToArray();

        bool mismatch = editor.ReplaceCode(methodName: "length", methodDescriptor: null, codeOffset: 0, [0xff], [0x00]);

        Assert.False(mismatch);
        Assert.Equal(original, editor.ToArray());

        bool replaced = editor.ReplaceCode(methodName: "length", methodDescriptor: null, codeOffset: 1, [Opcodes.InvokeVirtual], [Opcodes.Nop]);
        bool outside = editor.ReplaceCode(methodName: "length", methodDescriptor: null, codeLength, [0x00], [0x00]);
        ArgumentException lengths = Assert.Throws<ArgumentException>(() => Assert.True(editor.ReplaceCode(methodName: "length", methodDescriptor: null, codeOffset: 0, [0x2a], [0x2a, 0x00])));

        Assert.True(replaced);
        Assert.False(outside);
        Assert.Equal(Opcodes.Nop, editor.Model.GetCode(name: "length").Instructions[index: 1].Opcode);
        Assert.Equal(expected: "replacement", lengths.ParamName);
    }

    /// <summary>String and numeric constants are replaced at the constant, so every use changes and unrelated constants do not.</summary>
    [Fact]
    public void ReplacesConstants()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);

        int strings = editor.ReplaceString(oldValue: "hello", newValue: "goodbye");
        int integers = editor.ReplaceInteger(oldValue: 100000, newValue: 7);
        int longs = editor.ReplaceLong(oldValue: 5000000000L, newValue: 9);
        int floats = editor.ReplaceFloat(oldValue: 2.5f, newValue: 0.5f);
        int doubles = editor.ReplaceDouble(oldValue: 3.5, newValue: 0.25);
        int absent = editor.ReplaceString(oldValue: "absent", newValue: "x");
        JavaClassFile file = JavaClassFile.Read(editor.ToArray());

        Assert.Equal(expected: 5, strings + integers + longs + floats + doubles);
        Assert.Equal(expected: 0, absent);
        Assert.Contains(expected: "goodbye", file.GetStrings());
        Assert.Contains(expected: "hello again", file.GetStrings());
        Assert.DoesNotContain(expected: "hello", file.GetStrings());
        Assert.Equal(expected: 1, file.CountOf<IntegerEntry>(static entry => entry.Value == 7));
        Assert.Equal(expected: 1, file.CountOf<LongEntry>(static entry => entry.Value == 9));
        Assert.Equal(expected: 1, file.CountOf<FloatEntry>(static entry => entry.Value == 0.5f));
        Assert.Equal(expected: 1, file.CountOf<DoubleEntry>(static entry => entry.Value == 0.25));
    }

    /// <summary>Scoping to one method leaves the same call elsewhere alone.</summary>
    [Fact]
    public void ReplacesInvocationsOnlyInTheRequestedMethod()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);

        int replaced = editor.ReplaceInvocation(CollectionsSort, toOwner: "fixture/Helper", methodName: "sortOnce", methodDescriptor: SortDescriptor);

        Assert.Equal(expected: 1, replaced);
        Assert.All(StaticCalls(editor, method: "sortTwice"), static call => Assert.Equal(expected: "java/util/Collections", call.Owner));
        Assert.All(StaticCalls(editor, method: "sortOnce"), static call => Assert.Equal(expected: "fixture/Helper", call.Owner));
    }

    /// <summary>A static call becomes a static call of another owner at the same length, at every matching site.</summary>
    [Fact]
    public void ReplacesStaticInvocationsInPlace()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);
        int[] lengthsBefore = AllInstructionLengths(editor);

        int replaced = editor.ReplaceInvocation(CollectionsSort, toOwner: "fixture/Helper");

        Assert.Equal(expected: 3, replaced);
        Assert.Equal(lengthsBefore, AllInstructionLengths(editor));
        MemberReference expected = new(ConstantTag.Methodref, Owner: "fixture/Helper", Name: "sort", SortDescriptor);
        Assert.All(StaticCalls(editor, method: "sortTwice").Concat(StaticCalls(editor, method: "sortOnce")), call => Assert.Equal(expected, call));
        Assert.Equal(
            expected: 1,
            editor.Model.CountOf<MemberReferenceEntry>(
                entry => editor.Model.GetMemberReference(entry.Index) == new MemberReference(ConstantTag.Methodref, Owner: "java/util/Collections", Name: "sort", SortDescriptor)
            )
        );
    }

    /// <summary>A pool that is full is reported instead of corrupting the class.</summary>
    [Fact]
    public void ReportsAFullConstantPool()
    {
        ClassFileWriter writer = new(name: "pkg/Full");

        for (int value = 0; value < 65530; value++)
            Assert.True(writer.AddInteger(value) > 0);

        ClassFileEditor editor = new(writer.ToArray());

        InvalidOperationException narrow = Assert.Throws<InvalidOperationException>(() => Assert.True(editor.AddInteger(value: -1) > 0));
        InvalidOperationException wide = Assert.Throws<InvalidOperationException>(() => Assert.True(editor.AddLong(value: -1) > 0));

        Assert.Contains(expectedSubstring: "full", narrow.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "full", wide.Message, StringComparison.Ordinal);
    }

    /// <summary>Retargeting a reference constant moves every instruction that uses it.</summary>
    [Fact]
    public void RetargetsAMemberReferenceEverywhere()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);
        MemberReference reference = new(ConstantTag.Methodref, Owner: "java/util/Collections", Name: "sort", SortDescriptor);

        int retargeted = editor.RetargetMemberReference(reference, toOwner: "fixture/Helper", toName: "stableSort");
        int again = editor.RetargetMemberReference(reference, toOwner: "x/Y");

        Assert.Equal(expected: 1, retargeted);
        Assert.Equal(expected: 0, again);
        Assert.All(
            StaticCalls(editor, method: "sortTwice").Concat(StaticCalls(editor, method: "sortOnce")),
            call => Assert.Equal(new MemberReference(ConstantTag.Methodref, Owner: "fixture/Helper", Name: "stableSort", SortDescriptor), call)
        );
        Assert.Equal(
            expected: 0,
            editor.Model.CountOf<MemberReferenceEntry>(entry => editor.Model.GetMemberReference(entry.Index).Owner == "java/util/Collections")
        );
    }

    /// <summary>One operand of one instruction can be pointed at another constant.</summary>
    [Fact]
    public void RewritesOneConstantOperand()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);
        int replacement = editor.AddString(value: "replacement");

        editor.RewriteConstantOperand(methodName: "greeting", methodDescriptor: null, instructionOffset: 0, replacement);

        Instruction load = editor.Model.GetCode(name: "greeting").Instructions[index: 0];
        Instruction untouched = editor.Model.GetCode(name: "other").Instructions[index: 0];
        Assert.Equal(replacement, load.ConstantIndex);
        Assert.Equal(expected: "replacement", editor.Model.GetUtf8(editor.Model.GetEntry<StringEntry>(load.ConstantIndex ?? 0).StringIndex));
        Assert.Equal(expected: "hello again", editor.Model.GetUtf8(editor.Model.GetEntry<StringEntry>(untouched.ConstantIndex ?? 0).StringIndex));
    }

    /// <summary>Pointing an invokeinterface at another method updates its argument count, which the JVM verifies against the descriptor.</summary>
    [Fact]
    public void RewritingAnInvokeInterfaceOperandUpdatesItsArgumentCount()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);
        CodeAttribute code = editor.Model.GetCode(name: "size");
        Instruction call = code.Instructions.Single(static instruction => instruction.Opcode == Opcodes.InvokeInterface);
        int offer = editor.AddInterfaceMethodReference(owner: "java/util/concurrent/BlockingQueue", name: "offer", descriptor: "(Ljava/lang/Object;JLjava/util/concurrent/TimeUnit;)Z");

        editor.RewriteConstantOperand(methodName: "size", methodDescriptor: null, call.Offset, offer);

        byte[] edited = editor.ToArray();
        Assert.Equal(expected: 5, edited[editor.Model.GetCode(name: "size").CodeOffset + call.Offset + 3]);
        Assert.Equal(
            offer,
            editor.Model.GetCode(name: "size").Instructions.Single(static instruction => instruction.Opcode == Opcodes.InvokeInterface).ConstantIndex
        );
    }

    /// <summary>A virtual call passes its receiver as the first parameter of the static replacement.</summary>
    [Fact]
    public void VirtualInvocationsTakeTheReceiverAsFirstParameter()
    {
        ClassFileEditor editor = new(ClassFileFixtures.Sample);

        int replaced = editor.ReplaceInvocation(JavaMethod.Virtual(owner: "java/lang/String", name: "length", descriptor: "()I"), toOwner: "fixture/Helper");

        Assert.Equal(expected: 1, replaced);
        Instruction call = editor.Model.GetCode(name: "length").Instructions.Single(static instruction => instruction.Opcode == Opcodes.InvokeStatic);
        Assert.Equal(expected: 3, call.Length);
        Assert.Equal(
            new MemberReference(ConstantTag.Methodref, Owner: "fixture/Helper", Name: "length", Descriptor: "(Ljava/lang/String;)I"),
            editor.Model.GetMemberReference(call.ConstantIndex ?? 0)
        );
    }

    private static int[] AllInstructionLengths(ClassFileEditor editor)
    {
        return [.. editor.Model.Methods.SelectMany(static method => method.Code?.Instructions ?? []).Select(static instruction => instruction.Length)];
    }

    private static MemberReference[] StaticCalls(ClassFileEditor editor, string method)
    {
        return [.. editor.Model.GetCode(method).Instructions.Where(instruction => instruction.Opcode == Opcodes.InvokeStatic).Select(instruction => editor.Model.GetMemberReference(instruction.ConstantIndex ?? 0))];
    }
}

using System.Reflection;

using JvmBridge.ClassFiles;
using JvmBridge.ClassFiles.Code;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies the named opcodes, the code builder and the instruction matchers, which let agents and tests write and match bytecode without byte literals.</summary>
public sealed class CodeBuilderTests
{
    private static readonly JavaMethod Length = JavaMethod.Virtual(owner: "java/lang/String", name: "length", descriptor: "()I");

    /// <summary>A class file of version 51 or later needs stack map frames at branch targets, which the writer does not write, so a branching body is refused there and accepted at version 50.</summary>
    [Fact]
    public void BranchesNeedAClassOfVersion50OrOlder()
    {
        static CodeBuilder Loop(CodeBuilder code)
        {
            CodeLabel top = code.NewLabel();

            return code.Mark(top).Goto(top);
        }

        ArgumentException refused = Assert.Throws<ArgumentException>(static () => new ClassFileWriter(name: "test/Loop").AddMethod(name: "spin", descriptor: "()V", Loop));
        byte[] accepted = new ClassFileWriter(name: "test/Loop", majorVersion: 50).AddMethod(name: "spin", descriptor: "()V", Loop).ToArray();

        Assert.Contains(expectedSubstring: "stack map frames", refused.Message, StringComparison.Ordinal);
        Assert.Equal([Opcodes.Goto], Method(accepted, name: "spin").Instructions.Select(static instruction => instruction.Opcode));
    }

    /// <summary>Branches resolve to the offsets of their labels, and the stack depth a branch carries is the one at its label, which the code that follows starts from.</summary>
    [Fact]
    public void BranchesPointAtTheirLabels()
    {
        // static int sign(int value) { return value < 0 ? -1 : 1; }
        byte[] bytes = new ClassFileWriter(name: "test/Sign", majorVersion: 50)
            .AddMethod(
                name: "sign",
                descriptor: "(I)I",
                static code =>
                {
                    CodeLabel negative = code.NewLabel();
                    CodeLabel done = code.NewLabel();

                    return code.ILoad(slot: 0).If(Opcodes.IfLt, negative).IConst(value: 1).Goto(done).Mark(negative).IConst(value: -1).Mark(done).IReturn();
                }
            )
            .ToArray();

        CodeAttribute code = Method(bytes, name: "sign");
        Instruction branch = code.Instructions[index: 1];
        Instruction jump = code.Instructions[index: 3];

        Assert.Equal(expected: 1, code.MaxStack);
        Assert.Equal(expected: 1, code.MaxLocals);
        Assert.Equal(
            [Opcodes.ILoad0, Opcodes.IfLt, Opcodes.IConst1, Opcodes.Goto, Opcodes.IConstM1, Opcodes.IReturn],
            code.Instructions.Select(static instruction => instruction.Opcode)
        );
        Assert.Equal(expected: 8, branch.Offset + Offset(bytes, code, branch));
        Assert.Equal(expected: 9, jump.Offset + Offset(bytes, code, jump));
    }

    /// <summary>A builder that pops more than the stack holds, writes code no path reaches, or branches to a label it never places, fails with what is wrong instead of writing a method the verifier rejects.</summary>
    [Fact]
    public void BuilderRejectsInconsistentCode()
    {
        ClassFileWriter writer = new(name: "test/Broken", majorVersion: 50);

        InvalidOperationException underflow = Assert.Throws<InvalidOperationException>(() => writer.AddMethod(name: "pop", descriptor: "()V", static code => code.Pop().Return()));
        InvalidOperationException unreachable = Assert.Throws<InvalidOperationException>(() => writer.AddMethod(name: "dead", descriptor: "()V", static code => code.Return().Nop()));
        InvalidOperationException unplaced = Assert.Throws<InvalidOperationException>(() => writer.AddMethod(name: "lost", descriptor: "()V", static code => code.Goto(code.NewLabel())));

        Assert.Contains(expectedSubstring: "stack has 0", underflow.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "never run", unreachable.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "never placed", unplaced.Message, StringComparison.Ordinal);
    }

    /// <summary>Constants take the shortest instruction, and the stack depth counts two slots for a long or double: what javac would write.</summary>
    [Fact]
    public void ConstantsTakeTheShortestForm()
    {
        byte[] bytes = new ClassFileWriter(name: "test/Constants")
            .AddMethod(
                name: "values",
                descriptor: "()V",
                static code => code.IConst(value: 5)
                    .IConst(value: 100)
                    .IConst(value: 1000)
                    .IConst(value: 100_000)
                    .LConst(value: 1)
                    .LConst(value: 7)
                    .Op(Opcodes.Pop2)
                    .Op(Opcodes.Pop2)
                    .Pop()
                    .Pop()
                    .Pop()
                    .Pop()
                    .Return()
            )
            .ToArray();

        CodeAttribute code = Method(bytes, name: "values");
        byte[] expected = [Opcodes.IConst5, Opcodes.BIPush, Opcodes.SIPush, Opcodes.Ldc, Opcodes.LConst1, Opcodes.Ldc2W, Opcodes.Pop2, Opcodes.Pop2, Opcodes.Pop, Opcodes.Pop, Opcodes.Pop, Opcodes.Pop, Opcodes.Return];

        Assert.Equal(expected, code.Instructions.Select(static instruction => instruction.Opcode));
        Assert.Equal(expected: 8, code.MaxStack);
        Assert.Equal(expected: 0, code.MaxLocals);
    }

    /// <summary>The editor assembles instructions against its own constant pool, for comparing with a method's code or replacing it, and refuses branches, which would need frames it does not write.</summary>
    [Fact]
    public void EditorAssemblesAgainstItsPool()
    {
        byte[] original = new ClassFileWriter(name: "test/Subject").AddMethod(name: "size", descriptor: "(Ljava/lang/String;)I", static code => code.ALoad(slot: 0).Invoke(Length).IReturn()).ToArray();
        ClassFileEditor editor = new(original);

        byte[] call = editor.AssembleCode(static code => code.Invoke(Length));
        ArgumentException refused = Assert.Throws<ArgumentException>(() => editor.AssembleCode(static code => code.Goto(code.NewLabel())));

        Assert.Equal(Opcodes.InvokeVirtual, call[0]);
        Assert.Equal(original, editor.ToArray());
        Assert.Contains(expectedSubstring: "cannot branch", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A local variable past slot 3 takes its one-byte operand form, past 255 the <c>wide</c> form, and a long or double counts two slots towards <c>max_locals</c>.</summary>
    [Fact]
    public void LocalsTakeTheirForms()
    {
        byte[] bytes = new ClassFileWriter(name: "test/Locals", majorVersion: 50)
            .AddMethod(
                name: "locals",
                descriptor: "(J)V",
                static code => code.LLoad(slot: 0).LStore(slot: 4).IConst(value: 1).IStore(slot: 300).IInc(slot: 300, delta: 2).Return()
            )
            .ToArray();

        CodeAttribute code = Method(bytes, name: "locals");

        Assert.Equal(
            [Opcodes.LLoad0, Opcodes.LStore, Opcodes.IConst1, Opcodes.Wide, Opcodes.Wide, Opcodes.Return],
            code.Instructions.Select(static instruction => instruction.Opcode)
        );
        Assert.Equal(expected: 301, code.MaxLocals);
        Assert.Equal(expected: 2, code.MaxStack);
    }

    /// <summary>The instruction matchers find a call, a field access and a class operand by name, without reading the constant pool by hand.</summary>
    [Fact]
    public void MatchersRecognizeCallsFieldsAndClasses()
    {
        // static int measure(String value) { System.out.getClass(); return new StringBuilder(value).toString().length(); }
        byte[] bytes = new ClassFileWriter(name: "test/Matched")
            .AddMethod(
                name: "measure",
                descriptor: "(Ljava/lang/String;)I",
                static code => code.GetStatic(owner: "java/lang/System", name: "out", descriptor: "Ljava/io/PrintStream;")
                    .Pop()
                    .New(className: "java/lang/StringBuilder")
                    .Dup()
                    .ALoad(slot: 0)
                    .InvokeSpecial(owner: "java/lang/StringBuilder", name: "<init>", descriptor: "(Ljava/lang/String;)V")
                    .Invoke(JavaMethod.Virtual(owner: "java/lang/StringBuilder", name: "toString", descriptor: "()Ljava/lang/String;"))
                    .Invoke(Length)
                    .IReturn()
            )
            .ToArray();

        JavaClassFile file = JavaClassFile.Read(bytes);
        CodeAttribute code = Method(bytes, name: "measure");

        Assert.Equal(expected: 3, code.MaxStack);
        Assert.Equal(expected: 1, code.Instructions.Count(instruction => file.Invokes(instruction, Length)));
        Assert.Equal(expected: 1, code.Instructions.Count(instruction => file.AccessesField(instruction, owner: "java/lang/System", name: "out")));
        Assert.Equal(expected: 1, code.Instructions.Count(instruction => file.ReferencesClass(instruction, className: "java/lang/StringBuilder")));
        Assert.DoesNotContain(
            code.Instructions,
            instruction => file.Invokes(instruction, JavaMethod.Interface(owner: "java/lang/String", name: "length", descriptor: "()I"))
        );
    }

    /// <summary>The names cover the 202 opcodes from <c>nop</c> to <c>jsr_w</c>, one constant each, with the values of the JVM specification.</summary>
    [Fact]
    public void OpcodesNameEveryInstruction()
    {
        FieldInfo[] fields = typeof(Opcodes).GetFields(BindingFlags.Public | BindingFlags.Static);

        Assert.Equal(expected: 202, fields.Length);
        Assert.All(fields, static field => Assert.True(field.IsLiteral && field.FieldType == typeof(byte), field.Name));
        Assert.Equal(expected: 0x2a, Opcodes.ALoad0);
        Assert.Equal(expected: 0x9f, Opcodes.IfICmpEq);
        Assert.Equal(expected: 0xb6, Opcodes.InvokeVirtual);
        Assert.Equal(expected: 0xc9, Opcodes.JsrW);
    }

    private static CodeAttribute Method(byte[] bytes, string name)
    {
        return JavaClassFile.Read(bytes).FindMethod(name)?.Code ?? throw new InvalidOperationException(message: "The method has no code.");
    }

    private static short Offset(byte[] bytes, CodeAttribute code, Instruction branch)
    {
        int at = code.CodeOffset + branch.Offset + 1;

        return (short)((bytes[at] << 8) | bytes[at + 1]);
    }
}

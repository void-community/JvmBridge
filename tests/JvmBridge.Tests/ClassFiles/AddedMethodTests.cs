using JvmBridge.ClassFiles;
using JvmBridge.ClassFiles.Code;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies the methods that <see cref="ClassFileEditor.AddMethod(int, string, string, Func{CodeBuilder, CodeBuilder})"/> appends to a class it edits.</summary>
public sealed class AddedMethodTests
{
    private const string Connection = "pkg/Connection";

    private const int PrivateStaticSynthetic = 0x100A;

    private static readonly JavaMethod Process = JavaMethod.Virtual(Connection, name: "processReceivedPackets", descriptor: "()V");

    private static readonly JavaMethod IsOpen = JavaMethod.Virtual(Connection, name: "isChannelOpen", descriptor: "()Z");

    /// <summary>The method gets the flags, the code and the stack and local sizes the builder worked out, and the calls in it name the references the class already has.</summary>
    [Fact]
    public void AnAddedMethodGetsItsCodeAndSizes()
    {
        byte[] original = Controller(majorVersion: 50);
        ClassFileEditor editor = new(original);
        int constants = editor.Model.ConstantPool.Count;

        editor.AddMethod(
            PrivateStaticSynthetic,
            name: "agent$process",
            descriptor: "(Lpkg/Connection;)Z",
            static code => code.ALoad(slot: 0).Invoke(Process).ALoad(slot: 0).Invoke(IsOpen).IReturn()
        );

        ClassMember method = editor.Model.FindMethod(name: "agent$process", descriptor: "(Lpkg/Connection;)Z") ?? throw new InvalidOperationException(message: "The method was not added.");
        CodeAttribute code = method.Code ?? throw new InvalidOperationException(message: "The method has no code.");

        Assert.Equal(PrivateStaticSynthetic, method.AccessFlags);
        Assert.Equal(expected: 1, code.MaxStack);
        Assert.Equal(expected: 1, code.MaxLocals);
        Assert.Equal(
            [Opcodes.ALoad0, Opcodes.InvokeVirtual, Opcodes.ALoad0, Opcodes.InvokeVirtual, Opcodes.IReturn],
            code.Instructions.Select(static instruction => instruction.Opcode)
        );
        Assert.Equal(constants + 2, editor.Model.ConstantPool.Count);
        Assert.NotNull(editor.Model.FindMethod(name: "update", descriptor: "(Lpkg/Connection;)V"));
    }

    /// <summary>An instance method has <c>this</c> in its first local, before its parameters.</summary>
    [Fact]
    public void AnInstanceMethodCountsItsReceiver()
    {
        ClassFileEditor editor = new(Controller(majorVersion: 52));

        editor.AddMethod(accessFlags: 0x1002, name: "agent$wide", descriptor: "(J)J", static code => code.LLoad(slot: 1).LReturn());

        CodeAttribute code = editor.Model.FindMethod(name: "agent$wide")?.Code ?? throw new InvalidOperationException(message: "The method has no code.");

        Assert.Equal(expected: 2, code.MaxStack);
        Assert.Equal(expected: 3, code.MaxLocals);
    }

    /// <summary>A class of version 50 is verified without stack map frames, so the added code may branch there.</summary>
    [Fact]
    public void CodeMayBranchInAClassOfVersion50()
    {
        ClassFileEditor editor = new(Controller(majorVersion: 50));

        editor.AddMethod(PrivateStaticSynthetic, name: "agent$isNull", descriptor: "(Ljava/lang/Object;)Z", IsNull);

        CodeAttribute code = editor.Model.FindMethod(name: "agent$isNull")?.Code ?? throw new InvalidOperationException(message: "The method has no code.");

        Assert.Contains(code.Instructions, static instruction => instruction.Opcode == Opcodes.IfNull);
    }

    /// <summary>From version 51 a branch needs a stack map frame, which the editor does not write, so the method is refused and the constants its code named are gone again.</summary>
    [Fact]
    public void CodeThatBranchesIsRefusedFromVersion51()
    {
        byte[] original = Controller(majorVersion: 51);
        ClassFileEditor editor = new(original);

        ArgumentException failure = Assert.Throws<ArgumentException>(() => editor.AddMethod(PrivateStaticSynthetic, name: "agent$isNull", descriptor: "(Ljava/lang/Object;)Z", IsNull));

        Assert.Contains(expectedSubstring: "version 51", failure.Message, StringComparison.Ordinal);
        Assert.Equal(original, editor.ToArray());
    }

    /// <summary>A method the class declares already, a method without code and a malformed descriptor are refused without changing the class.</summary>
    [Fact]
    public void MethodsThatCannotBeAddedAreRefused()
    {
        byte[] original = Controller(majorVersion: 50);
        ClassFileEditor editor = new(original);

        InvalidOperationException declared = Assert.Throws<InvalidOperationException>(() => editor.AddMethod(PrivateStaticSynthetic, name: "update", descriptor: "(Lpkg/Connection;)V", static code => code.Return()));
        ArgumentException native = Assert.Throws<ArgumentException>(() => editor.AddMethod(accessFlags: 0x0108, name: "agent$native", descriptor: "()V", static code => code.Return()));
        ArgumentException malformed = Assert.Throws<ArgumentException>(() => editor.AddMethod(PrivateStaticSynthetic, name: "agent$bad", descriptor: "(Q)V", static code => code.Return()));
        ArgumentException constructor = Assert.Throws<ArgumentException>(() => editor.AddMethod(accessFlags: 0x0001, name: "<init>", descriptor: "(I)V", static code => code.Return()));

        Assert.Contains(expectedSubstring: "already declares", declared.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "native", native.Message, StringComparison.Ordinal);
        Assert.Equal(expected: "descriptor", malformed.ParamName);
        Assert.Equal(expected: "name", constructor.ParamName);
        Assert.Equal(original, editor.ToArray());
    }

    // static void update(Connection connection) { if (connection.isChannelOpen()) connection.processReceivedPackets(); }, written without a branch: processReceivedPackets() then isChannelOpen().
    private static byte[] Controller(int majorVersion)
    {
        return new ClassFileWriter(name: "pkg/Controller", majorVersion: majorVersion)
        .AddMethod(
            name: "update",
            descriptor: "(Lpkg/Connection;)V",
            static code => code.ALoad(slot: 0).Invoke(Process).ALoad(slot: 0).Invoke(IsOpen).Pop().Return()
        )
        .ToArray();
    }

    private static CodeBuilder IsNull(CodeBuilder code)
    {
        CodeLabel isNull = code.NewLabel();

        return code.ALoad(slot: 0).IfNull(isNull).IConst(value: 0).IReturn().Mark(isNull).IConst(value: 1).IReturn();
    }
}

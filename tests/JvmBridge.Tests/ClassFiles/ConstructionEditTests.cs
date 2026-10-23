using JvmBridge.Agents;
using JvmBridge.ClassFiles;
using JvmBridge.ClassFiles.Code;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies the declarative edits that replace a construction with another class or a factory, and that remove calls of methods returning their receiver.</summary>
public sealed class ConstructionEditTests
{
    private const string Holder = "test/Holder";

    private static readonly JavaMethod AutoReload = JavaMethod.Virtual(owner: "test/Builder", name: "autoreload", descriptor: "()Ltest/Builder;");

    private static readonly JavaMethod AutoSave = JavaMethod.Virtual(owner: "test/Builder", name: "autosave", descriptor: "()Ltest/Builder;");

    private static readonly JavaMethod NewKeySet = JavaMethod.Static(
        owner: "java/util/concurrent/ConcurrentHashMap",
        name: "newKeySet",
        descriptor: "()Ljava/util/concurrent/ConcurrentHashMap$KeySetView;"
    );

    // static void fill() { items = new ArrayList(); other = new ArrayList(4); names = new HashSet(); copy = new ArrayList(new ArrayList()); }
    // static Builder chain(Builder builder) { return builder.autosave().autoreload(); }
    private static readonly byte[] Subject = new ClassFileWriter(Holder)
        .AddMethod(
            name: "fill",
            descriptor: "()V",
            static code => Store(
                code.New(className: "java/util/ArrayList").Dup().InvokeSpecial(owner: "java/util/ArrayList", name: "<init>", descriptor: "()V"),
                field: "items",
                type: "Ljava/util/List;"
            )
                .New(className: "java/util/ArrayList")
                .Dup()
                .IConst(value: 4)
                .InvokeSpecial(owner: "java/util/ArrayList", name: "<init>", descriptor: "(I)V")
                .PutStatic(Holder, name: "other", descriptor: "Ljava/util/List;")
                .New(className: "java/util/HashSet")
                .Dup()
                .InvokeSpecial(owner: "java/util/HashSet", name: "<init>", descriptor: "()V")
                .PutStatic(Holder, name: "names", descriptor: "Ljava/util/Set;")
                .New(className: "java/util/ArrayList")
                .Dup()
                .New(className: "java/util/ArrayList")
                .Dup()
                .InvokeSpecial(owner: "java/util/ArrayList", name: "<init>", descriptor: "()V")
                .InvokeSpecial(owner: "java/util/ArrayList", name: "<init>", descriptor: "(Ljava/util/Collection;)V")
                .PutStatic(Holder, name: "copy", descriptor: "Ljava/util/List;")
                .Return()
        )
        .AddMethod(
            name: "chain",
            descriptor: "(Ltest/Builder;)Ltest/Builder;",
            static code => code.ALoad(slot: 0).Invoke(AutoSave).Invoke(AutoReload).AReturn()
        )
        .ToArray();

    /// <summary>A factory whose object would go into a field of a primitive type, which no object fits, makes the patch skip the class with both types named.</summary>
    [Fact]
    public void AFactoryMustFitTheField()
    {
        byte[] primitive = new ClassFileWriter(Holder)
            .AddMethod(
                name: "count",
                descriptor: "()V",
                static code => code.New(className: "java/util/HashSet").Dup().InvokeSpecial(owner: "java/util/HashSet", name: "<init>", descriptor: "()V").PutStatic(Holder, name: "size", descriptor: "I").Return()
            )
            .ToArray();

        ClassPatchResult result = ClassPatch.For(Holder).ReplaceConstruction(fromClass: "java/util/HashSet", NewKeySet, storedIn: "size").Apply(primitive);

        Assert.False(result.Changed);
        Assert.Contains(expectedSubstring: "which the field size of type I cannot hold", result.SkippedReason, StringComparison.Ordinal);
    }

    /// <summary>A construction that takes arguments has no factory equivalent, so a factory replacement leaves it alone and says that nothing matched.</summary>
    [Fact]
    public void AFactoryOnlyReplacesConstructionsWithoutArguments()
    {
        ClassPatchResult result = ClassPatch.For(Holder).ReplaceConstruction(fromClass: "java/util/ArrayList", NewKeySet, storedIn: "other").Apply(Subject);

        Assert.False(result.Changed);
        Assert.Equal(expected: "no construction of java/util/ArrayList stored in other", result.SkippedReason);
    }

    /// <summary>A factory turns <c>new; dup; invokespecial</c> into <c>invokestatic</c> and four <c>nop</c>s, so the object it returns goes into the field as the constructed one did.</summary>
    [Fact]
    public void AFactoryReplacesAConstructionWithoutArguments()
    {
        ClassPatchResult result = ClassPatch.For(Holder).ReplaceConstruction(fromClass: "java/util/HashSet", NewKeySet, storedIn: "names").Apply(Subject);
        JavaClassFile file = JavaClassFile.Read(result.Bytes ?? []);
        IReadOnlyList<Instruction> instructions = Code(file, name: "fill").Instructions;
        int call = Index(instructions, instruction => file.Invokes(instruction, NewKeySet));

        Assert.Equal(expected: 1, result.ReplacedConstructions);
        Assert.Equal(
            ["new java/util/HashSet() replaced by java/util/concurrent/ConcurrentHashMap.newKeySet() stored in names"],
            result.Details.Select(static detail => detail[(detail.IndexOf(value: ' ', StringComparison.Ordinal) + 1)..])
        );
        Assert.Equal(
            [Opcodes.InvokeStatic, Opcodes.Nop, Opcodes.Nop, Opcodes.Nop, Opcodes.Nop, Opcodes.PutStatic],
            instructions.Skip(call).Take(count: 6).Select(static instruction => instruction.Opcode)
        );
        Assert.DoesNotContain(instructions, instruction => file.ReferencesClass(instruction, className: "java/util/HashSet"));
    }

    /// <summary>A construction nested in the arguments of another of the same class is matched to its own constructor call, so both keep their descriptors with the new class.</summary>
    [Fact]
    public void NestedConstructionsKeepTheirConstructors()
    {
        ClassPatchResult result = ClassPatch.For(Holder).ReplaceConstruction(fromClass: "java/util/ArrayList", toClass: "java/util/LinkedList", storedIn: "copy").Apply(Subject);
        string text = ClassFileDisassembler.Disassemble(result.Bytes ?? [], method: "fill");

        Assert.Equal(expected: 1, result.ReplacedConstructions);
        Assert.Contains(expectedSubstring: "java/util/LinkedList.<init>:(Ljava/util/Collection;)V", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "java/util/ArrayList.<init>:()V", text, StringComparison.Ordinal);
    }

    /// <summary>Only a call with a receiver, no arguments and its owner as the result can be removed without changing the stack, so anything else is refused when the patch is built.</summary>
    [Fact]
    public void RemovalNeedsACallThatReturnsItsReceiver()
    {
        ArgumentException arguments = Assert.Throws<ArgumentException>(
            static () => ClassPatch.For(Holder).RemoveInvocation(JavaMethod.Virtual(owner: "test/Builder", name: "depth", descriptor: "(I)Ltest/Builder;"))
        );

        ArgumentException isStatic = Assert.Throws<ArgumentException>(
            static () => ClassPatch.For(Holder).RemoveInvocation(JavaMethod.Static(owner: "test/Builder", name: "create", descriptor: "()Ltest/Builder;"))
        );

        Assert.Contains(expectedSubstring: "()Ltest/Builder;", arguments.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "static", isStatic.Message, StringComparison.Ordinal);
    }

    /// <summary>Calls that return their receiver disappear as <c>nop</c>s, so the receiver stands for each result, and the result counts them.</summary>
    [Fact]
    public void RemovesCallsThatReturnTheirReceiver()
    {
        ClassPatchResult result = ClassPatch.For(Holder).Method(name: "chain").RemoveInvocation(AutoSave).RemoveInvocation(AutoReload).Apply(Subject);
        IReadOnlyList<Instruction> instructions = Code(JavaClassFile.Read(result.Bytes ?? []), name: "chain").Instructions;

        Assert.Equal(expected: 2, result.RemovedInvocations);
        Assert.Equal(
            [Opcodes.ALoad0, Opcodes.Nop, Opcodes.Nop, Opcodes.Nop, Opcodes.Nop, Opcodes.Nop, Opcodes.Nop, Opcodes.AReturn],
            instructions.Select(static instruction => instruction.Opcode)
        );
        Assert.Contains(result.Details, static detail => detail == "chain: 1 invocation of test/Builder.autosave removed");
    }

    /// <summary>Without a field, every construction of the class in the scope is replaced, each keeping its own constructor descriptor.</summary>
    [Fact]
    public void ReplacesEveryConstructionWithoutAField()
    {
        ClassPatchResult result = ClassPatch.For(Holder).Method(name: "fill").ReplaceConstruction(fromClass: "java/util/ArrayList", toClass: "java/util/Vector").Apply(Subject);
        string text = ClassFileDisassembler.Disassemble(result.Bytes ?? [], method: "fill");

        Assert.Equal(expected: 4, result.ReplacedConstructions);
        Assert.DoesNotContain(expectedSubstring: "java/util/ArrayList", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "java/util/Vector.<init>:(I)V", text, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "java/util/Vector.<init>:(Ljava/util/Collection;)V", text, StringComparison.Ordinal);
    }

    /// <summary>A construction becomes one of another class with the same constructor, only where the object goes into the named field, and other constructions of the class stay.</summary>
    [Fact]
    public void ReplacesTheConstructionStoredInAField()
    {
        ClassPatchResult result = ClassPatch.For(Holder).ReplaceConstruction(fromClass: "java/util/ArrayList", toClass: "java/util/concurrent/CopyOnWriteArrayList", storedIn: "items").Apply(Subject);
        JavaClassFile file = JavaClassFile.Read(result.Bytes ?? []);
        IReadOnlyList<Instruction> instructions = Code(file, name: "fill").Instructions;

        Assert.Equal(expected: 1, result.ReplacedConstructions);
        Assert.Equal(
            expected: 1,
            instructions.Count(instruction => file.ReferencesClass(instruction, className: "java/util/concurrent/CopyOnWriteArrayList"))
        );
        Assert.Equal(expected: 3, instructions.Count(instruction => file.ReferencesClass(instruction, className: "java/util/ArrayList")));
    }

    private static CodeAttribute Code(JavaClassFile file, string name)
    {
        return file.FindMethod(name)?.Code ?? throw new InvalidOperationException(message: "The method has no code.");
    }

    private static int Index(IReadOnlyList<Instruction> instructions, Func<Instruction, bool> match)
    {
        for (int index = 0; index < instructions.Count; index++)
        {
            if (match(instructions[index]))
                return index;
        }

        return -1;
    }

    private static CodeBuilder Store(CodeBuilder code, string field, string type)
    {
        return code.PutStatic(Holder, field, type);
    }
}

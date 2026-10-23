using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies the getters, setters and invokers that a patch adds so that helpers reach private members without reflection or opening them.</summary>
public sealed class MemberAdditionTests
{
    private const string Vault = "test/Vault";

    // class Vault { private String name; private static int count; private final long id; private String secret(int length) { return name; } private static int twice(int value) { return value; } }
    private static readonly byte[] Subject = new ClassFileWriter(Vault)
        .AddField(name: "name", descriptor: "Ljava/lang/String;")
        .AddField(name: "count", descriptor: "I", accessFlags: 0x000a)
        .AddField(name: "id", descriptor: "J", accessFlags: 0x0012)
        .AddMethod(
            name: "secret",
            descriptor: "(I)Ljava/lang/String;",
            static code => code.ALoad(slot: 0).GetField(Vault, name: "name", descriptor: "Ljava/lang/String;").AReturn(),
            accessFlags: 0x0002
        )
        .AddMethod(name: "twice", descriptor: "(I)I", static code => code.ILoad(slot: 0).IReturn(), accessFlags: 0x000a)
        .ToArray();

    /// <summary>A getter typed as a supertype reads the field with <c>getfield</c> and returns it as that type, public and synthetic, and the result counts it.</summary>
    [Fact]
    public void AddsAGetterOfAPrivateField()
    {
        ClassPatchResult result = ClassPatch.For(Vault).AddFieldGetter(field: "name", name: "agent$name", returns: "Ljava/lang/Object;").Apply(Subject);
        ClassMember getter = Find(result, name: "agent$name", descriptor: "()Ljava/lang/Object;");

        Assert.Equal(expected: 1, result.AddedMembers);
        Assert.Equal(expected: 0x1001, getter.AccessFlags);
        Assert.Equal([Opcodes.ALoad0, Opcodes.GetField, Opcodes.AReturn], CodeOf(getter));
        Assert.Equal(["agent$name()Ljava/lang/Object;: getter of field name added"], result.Details);
    }

    /// <summary>A setter that takes a supertype casts its argument to the field's type before it stores it.</summary>
    [Fact]
    public void AddsASetterThatCastsToTheField()
    {
        ClassMember setter = Find(
            ClassPatch.For(Vault).AddFieldSetter(field: "name", name: "agent$setName", accepts: "Ljava/lang/Object;").Apply(Subject),
            name: "agent$setName",
            descriptor: "(Ljava/lang/Object;)V"
        );

        Assert.Equal([Opcodes.ALoad0, Opcodes.ALoad1, Opcodes.CheckCast, Opcodes.PutField, Opcodes.Return], CodeOf(setter));
    }

    /// <summary>A static field gets a static getter, which needs no receiver.</summary>
    [Fact]
    public void AddsAStaticGetterForAStaticField()
    {
        ClassMember getter = Find(ClassPatch.For(Vault).AddFieldGetter(field: "count", name: "agent$count").Apply(Subject), name: "agent$count", descriptor: "()I");

        Assert.Equal(expected: 0x1009, getter.AccessFlags);
        Assert.Equal([Opcodes.GetStatic, Opcodes.IReturn], CodeOf(getter));
    }

    /// <summary>An invoker calls a private instance method on its receiver with <c>invokespecial</c>, and a private static one with <c>invokestatic</c>, each with the method's descriptor.</summary>
    [Fact]
    public void AddsInvokersOfPrivateMethods()
    {
        ClassPatchResult result = ClassPatch.For(Vault)
            .AddInvoker(method: "secret", descriptor: "(I)Ljava/lang/String;", name: "agent$secret")
            .AddInvoker(method: "twice", descriptor: "(I)I", name: "agent$twice")
            .Apply(Subject);

        Assert.Equal(expected: 2, result.AddedMembers);
        Assert.Equal(
            [Opcodes.ALoad0, Opcodes.ILoad1, Opcodes.InvokeSpecial, Opcodes.AReturn],
            CodeOf(Find(result, name: "agent$secret", descriptor: "(I)Ljava/lang/String;"))
        );
        Assert.Equal([Opcodes.ILoad0, Opcodes.InvokeStatic, Opcodes.IReturn], CodeOf(Find(result, name: "agent$twice", descriptor: "(I)I")));
    }

    /// <summary>Two additions of one name, a constructor invoker and an invalid type are refused when the patch is built.</summary>
    [Fact]
    public void RefusesInvalidAdditionsWhenThePatchIsBuilt()
    {
        ClassPatch getter = ClassPatch.For(Vault).AddFieldGetter(field: "name", name: "agent$name");

        Assert.True(
            Assert.Throws<InvalidOperationException>(() => getter.AddFieldGetter(field: "count", name: "agent$name")).Message.Contains(value: "already", StringComparison.Ordinal)
        );
        Assert.True(
            Assert.Throws<ArgumentException>(static () => ClassPatch.For(Vault).AddInvoker(method: "<init>", descriptor: "()V", name: "agent$create")).Message.Length > 0
        );
        Assert.True(
            Assert.Throws<ArgumentException>(static () => ClassPatch.For(Vault).AddFieldGetter(field: "name", name: "agent$name", returns: "java/lang/Object")).Message.Length > 0
        );
    }

    /// <summary>
    /// Without a field type the getter takes any field of the name, so one patch serves versions where the type differs; with one, a field of another type is not found. A primitive cannot be returned as an object, a final field has no setter, and a
    /// name the class declares already is not reused.
    /// </summary>
    [Fact]
    public void RefusesWhatCannotBeAdded()
    {
        ClassPatchResult wrongType = ClassPatch.For(Vault).AddFieldGetter(field: "name", name: "agent$name", fieldDescriptor: "Ljava/util/List;").Apply(Subject);
        ClassPatchResult boxed = ClassPatch.For(Vault).AddFieldGetter(field: "count", name: "agent$count", returns: "Ljava/lang/Object;").Apply(Subject);
        ClassPatchResult final = ClassPatch.For(Vault).AddFieldSetter(field: "id", name: "agent$setId").Apply(Subject);
        ClassPatchResult taken = ClassPatch.For(Vault).AddInvoker(method: "twice", descriptor: "(I)I", name: "twice").Apply(Subject);

        Assert.Equal(expected: "field name Ljava/util/List; not found", wrongType.SkippedReason);
        Assert.Contains(expectedSubstring: "cannot convert", boxed.SkippedReason, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "is final", final.SkippedReason, StringComparison.Ordinal);
        Assert.Equal(PatchSkipCategory.AlreadyAdvised, taken.SkipCategory);
    }

    private static IEnumerable<byte> CodeOf(ClassMember method)
    {
        return (method.Code ?? throw new InvalidOperationException(message: "The method has no code.")).Instructions.Select(static instruction => instruction.Opcode);
    }

    private static ClassMember Find(ClassPatchResult result, string name, string descriptor)
    {
        Assert.True(result.Changed, result.SkippedReason);

        return JavaClassFile.Read(result.Bytes ?? []).FindMethod(name, descriptor) ?? throw new InvalidOperationException($"{name}{descriptor} was not added.");
    }
}

using System.Text;

using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>
/// Verifies that <see cref="ClassFileEditor"/> reuses a constant by what it resolves to, in a pool with the duplicate entries that javac 8 writes: a second Class entry for a class
/// that a stack map frame names, which the member references of the code then go through, and Utf8 entries that hold one value twice.
/// </summary>
public sealed class ConstantReuseTests
{
    private const string AndThenDescriptor = "(Ljava/util/function/BiConsumer;)Ljava/util/function/BiConsumer;";

    private const string BiConsumer = "java/util/function/BiConsumer";

    private const string List = "java/util/ArrayList";

    private static readonly JavaMethod AndThen = JavaMethod.Interface(BiConsumer, name: "andThen", AndThenDescriptor);

    /// <summary>
    /// Built by hand, since neither writer nor editor makes duplicates. Its one method, <c>static void first(BiConsumer consumer) { consumer.andThen(consumer); }</c>, calls
    /// <c>andThen</c> through #11, whose owner is #7, the second Class entry of BiConsumer.
    /// </summary>
    private static readonly byte[] Duplicates = new RawClass()
        .Utf8(value: "test/Twice") // 1
        .Reference(ConstantTag.Class, first: 1) // 2
        .Utf8(value: "java/lang/Object") // 3
        .Reference(ConstantTag.Class, first: 3) // 4
        .Utf8(BiConsumer) // 5
        .Reference(ConstantTag.Class, first: 5) // 6, the one a stack map frame names
        .Reference(ConstantTag.Class, first: 5) // 7, the one the references go through
        .Utf8(value: "andThen") // 8
        .Utf8(AndThenDescriptor) // 9
        .Reference(ConstantTag.NameAndType, first: 8, second: 9) // 10
        .Reference(ConstantTag.InterfaceMethodref, first: 7, second: 10) // 11
        .Utf8(value: "run") // 12
        .Utf8(value: "()V") // 13
        .Utf8(value: "run") // 14
        .Reference(ConstantTag.NameAndType, first: 14, second: 13) // 15, through the second Utf8 of run
        .Utf8(List) // 16
        .Reference(ConstantTag.Class, first: 16) // 17
        .Utf8(List) // 18
        .Reference(ConstantTag.Class, first: 18) // 19, through the second Utf8 of the name
        .Reference(ConstantTag.Methodref, first: 19, second: 15) // 20
        .Utf8(value: "size") // 21
        .Utf8(value: "I") // 22
        .Reference(ConstantTag.NameAndType, first: 21, second: 22) // 23
        .Reference(ConstantTag.Fieldref, first: 19, second: 23) // 24
        .Utf8(value: "hello") // 25
        .Utf8(value: "hello") // 26
        .Reference(ConstantTag.String, first: 26) // 27
        .Utf8(value: "first") // 28
        .Utf8(value: "(Ljava/util/function/BiConsumer;)V") // 29
        .Utf8(value: "Code") // 30
        .ToClass(
            thisClass: 2,
            superClass: 4,
            methodName: 28,
            methodDescriptor: 29,
            codeName: 30,
            [0x2a, 0x2a, 0xb9, 0x00, 0x0b, 0x02, 0x00, 0x57, 0xb1]
        );

    /// <summary>A reference of another kind is not reused: it is added once, through the first Class entry of its owner and the existing NameAndType.</summary>
    [Fact]
    public void AReferenceOfAnotherKindIsAdded()
    {
        ClassFileEditor editor = new(Duplicates);

        int added = editor.AddMethodReference(BiConsumer, name: "andThen", AndThenDescriptor);
        MemberReferenceEntry entry = Assert.IsType<MemberReferenceEntry>(editor.Model.ConstantPool[added]);

        Assert.Equal(expected: 31, added);
        Assert.Equal(expected: 6, entry.ClassIndex);
        Assert.Equal(expected: 10, entry.NameAndTypeIndex);
        Assert.Equal(added, editor.AddMethodReference(BiConsumer, name: "andThen", AndThenDescriptor));
    }

    /// <summary>Code assembled for the call names the reference the method's code already uses, so the bytes match for a code replacement, and nothing is added to the class.</summary>
    [Fact]
    public void AssembledCodeMatchesTheCodeOfTheClass()
    {
        ClassFileEditor editor = new(Duplicates);

        byte[] assembled = editor.AssembleCode(static code => code.Invoke(AndThen));

        Assert.Equal([0xb9, 0x00, 0x0b, 0x02, 0x00], assembled);
        Assert.True(editor.ReplaceCode(methodName: "first", methodDescriptor: null, codeOffset: 2, assembled, assembled));
        Assert.Equal(Duplicates, editor.ToArray());
    }

    /// <summary>Each kind of constant that names others is found by the values it resolves to, whichever duplicate entry it goes through, and the class stays as it was.</summary>
    [Fact]
    public void ExistingConstantsAreFoundByWhatTheyResolveTo()
    {
        ClassFileEditor editor = new(Duplicates);

        Assert.Equal(expected: 6, editor.AddClass(BiConsumer));
        Assert.Equal(expected: 17, editor.AddClass(List));
        Assert.Equal(expected: 10, editor.AddNameAndType(name: "andThen", AndThenDescriptor));
        Assert.Equal(expected: 15, editor.AddNameAndType(name: "run", descriptor: "()V"));
        Assert.Equal(expected: 11, editor.AddInterfaceMethodReference(BiConsumer, name: "andThen", AndThenDescriptor));
        Assert.Equal(expected: 20, editor.AddMethodReference(List, name: "run", descriptor: "()V"));
        Assert.Equal(expected: 24, editor.AddFieldReference(List, name: "size", descriptor: "I"));
        Assert.Equal(expected: 27, editor.AddString(value: "hello"));
        Assert.Equal(Duplicates, editor.ToArray());
    }

    // Writes a version 52 class file with the constants given in order and one public static method.
    private sealed class RawClass
    {
        private readonly List<byte> _pool = [];
        private int _count = 1;

        internal RawClass Reference(ConstantTag tag, int first, int? second = null)
        {
            _pool.Add((byte)tag);
            WriteUInt16(_pool, first);

            if (second is int index)
                WriteUInt16(_pool, index);

            _count++;

            return this;
        }

        internal byte[] ToClass(int thisClass, int superClass, int methodName, int methodDescriptor, int codeName, byte[] code)
        {
            List<byte> file = [0xca, 0xfe, 0xba, 0xbe, 0x00, 0x00, 0x00, 0x34];

            WriteUInt16(file, _count);
            file.AddRange(_pool);

            // public super class, its superclass, no interfaces and no fields, and one public static method with one attribute.
            foreach (int value in new[] { 0x0021, thisClass, superClass, 0, 0, 1, 0x0009, methodName, methodDescriptor, 1, codeName })
                WriteUInt16(file, value);

            // The Code attribute: its length, max_stack 2, max_locals 1, the code, no exception table and no attributes; then no class attributes.
            WriteUInt32(file, 12 + code.Length);
            WriteUInt16(file, value: 2);
            WriteUInt16(file, value: 1);
            WriteUInt32(file, code.Length);
            file.AddRange(code);

            foreach (int value in new[] { 0, 0, 0 })
                WriteUInt16(file, value);

            return [.. file];
        }

        internal RawClass Utf8(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);

            _pool.Add((byte)ConstantTag.Utf8);
            WriteUInt16(_pool, bytes.Length);
            _pool.AddRange(bytes);
            _count++;

            return this;
        }

        private static void WriteUInt16(List<byte> bytes, int value)
        {
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        private static void WriteUInt32(List<byte> bytes, int value)
        {
            WriteUInt16(bytes, value >> 16);
            WriteUInt16(bytes, value);
        }
    }
}

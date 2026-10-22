using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies new classes produced by the class-file writer.</summary>
public sealed class ClassFileWriterTests
{
    // The hand-assembled helper class that HelloAgent used to build byte by byte.
    private const string HandWrittenCallbackClass = "cafebabe00000034000701001f6a766d6272696467652f73616d706c652f4167656e7443616c6c6261636b730700010100106a6176612f6c616e672f4f626a6563740700030100076f6e4672616d65010015284c6a6176612f6c616e672f4f626a6563743b295600210002000400000000000101090005000600000000";

    /// <summary>Constants are added once, Long and Double take two slots, and code references resolve.</summary>
    [Fact]
    public void DeduplicatesConstantsAndWritesCode()
    {
        ClassFileWriter writer = new(name: "pkg/Helper");
        int first = writer.AddMethodReference(owner: "java/lang/Math", name: "abs", descriptor: "(I)I");
        int again = writer.AddMethodReference(owner: "java/lang/Math", name: "abs", descriptor: "(I)I");
        int wide = writer.AddLong(1L << 40);
        int next = writer.AddInteger(value: 7);
        byte[] code = [0x1a, 0xb8, (byte)(first >> 8), (byte)first, 0xac];

        JavaClassFile file = JavaClassFile.Read(
            writer.AddMethod(name: "abs", descriptor: "(I)I", maxStack: 1, maxLocals: 1, code).AddMethod(name: "abs2", descriptor: "(I)I", maxStack: 1, maxLocals: 1, code).ToArray()
        );

        Instruction call = file.GetCode(name: "abs2").Instructions[index: 1];

        Assert.Equal(first, again);
        Assert.Equal(wide + 2, next);
        Assert.Null(file.ConstantPool[wide + 1]);
        Assert.Equal(expected: 1, file.CountOf<Utf8Entry>(static entry => entry.Value == "Code"));
        Assert.Equal(
            new MemberReference(ConstantTag.Methodref, Owner: "java/lang/Math", Name: "abs", Descriptor: "(I)I"),
            file.GetMemberReference(call.ConstantIndex ?? 0)
        );
    }

    /// <summary>Strings that need modified UTF-8 survive a round trip.</summary>
    [Fact]
    public void EncodesModifiedUtf8()
    {
        const string value = "héllo ☃ 😀 \u0000";
        ClassFileWriter writer = new(name: "pkg/Helper");
        int index = writer.AddString(value);

        JavaClassFile file = JavaClassFile.Read(writer.AddNativeMethod(name: "go", descriptor: "()V").ToArray());

        Assert.Equal(value, file.GetUtf8(file.GetEntry<StringEntry>(index).StringIndex));
    }

    /// <summary>Invalid arguments are rejected before anything is written.</summary>
    [Fact]
    public void RejectsInvalidArguments()
    {
        ClassFileWriter writer = new(name: "pkg/Helper");

        ArgumentException emptyName = Assert.Throws<ArgumentException>(() => new ClassFileWriter(name: ""));
        ArgumentException emptyCode = Assert.Throws<ArgumentException>(() => writer.AddMethod(name: "go", descriptor: "()V", maxStack: 0, maxLocals: 0, []));
        ArgumentException tooLong = Assert.Throws<ArgumentException>(() => Assert.True(writer.AddUtf8(new string(c: 'x', count: 70000)) > 0));
        ArgumentOutOfRangeException tooDeep = Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddMethod(name: "go", descriptor: "()V", maxStack: 70000, maxLocals: 0, [0xb1]));

        Assert.Equal(expected: "name", emptyName.ParamName);
        Assert.Equal(expected: "code", emptyCode.ParamName);
        Assert.Equal(expected: "value", tooLong.ParamName);
        Assert.Equal(expected: "maxStack", tooDeep.ParamName);
    }

    /// <summary>Duplicate members, illegal names, bad descriptors and flags that do not fit are rejected at the call, and a rejected call adds nothing.</summary>
    [Fact]
    public void RejectsInvalidMembersAtTheCall()
    {
        ClassFileWriter writer = new ClassFileWriter(name: "pkg/Helper").AddNativeMethod(name: "go", descriptor: "()V");
        int before = JavaClassFile.Read(writer.ToArray()).ConstantPool.Count;

        ArgumentException duplicate = Assert.Throws<ArgumentException>(() => writer.AddNativeMethod(name: "go", descriptor: "()V"));
        ArgumentException slash = Assert.Throws<ArgumentException>(() => writer.AddNativeMethod(name: "a/b", descriptor: "()V"));
        ArgumentException special = Assert.Throws<ArgumentException>(() => writer.AddNativeMethod(name: "<other>", descriptor: "()V"));
        ArgumentException descriptor = Assert.Throws<ArgumentException>(() => writer.AddNativeMethod(name: "run", descriptor: "V"));
        ArgumentOutOfRangeException flags = Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddNativeMethod(name: "run", descriptor: "()V", accessFlags: 0x10000));
        ArgumentOutOfRangeException version = Assert.Throws<ArgumentOutOfRangeException>(() => new ClassFileWriter(name: "pkg/Other", majorVersion: 70000));
        ClassFileWriter withInitializer = writer.AddNativeMethod(name: "<init>", descriptor: "()V");

        Assert.Same(writer, withInitializer);
        Assert.Equal(expected: "name", duplicate.ParamName);
        Assert.Equal(expected: "name", slash.ParamName);
        Assert.Equal(expected: "name", special.ParamName);
        Assert.Equal(expected: "descriptor", descriptor.ParamName);
        Assert.Equal(expected: "accessFlags", flags.ParamName);
        Assert.Equal(expected: "majorVersion", version.ParamName);
        Assert.Equal(before + 1, JavaClassFile.Read(writer.ToArray()).ConstantPool.Count);
    }

    /// <summary>A pool that is full reports it instead of corrupting the class.</summary>
    [Fact]
    public void ReportsAFullConstantPool()
    {
        ClassFileWriter writer = new(name: "pkg/Helper");

        for (int value = 0; value < 65530; value++)
            Assert.True(writer.AddInteger(value) > 0);

        InvalidOperationException full = Assert.Throws<InvalidOperationException>(() => Assert.True(writer.AddInteger(value: -1) > 0));

        Assert.Contains(expectedSubstring: "full", full.Message, StringComparison.Ordinal);
        Assert.Equal(expected: 65535, JavaClassFile.Read(writer.ToArray()).ConstantPool.Count);
    }

    /// <summary>A class of one native static method is exactly the class HelloAgent assembled by hand.</summary>
    [Fact]
    public void WritesTheHandAssembledNativeHelperClassByteForByte()
    {
        byte[] bytes = new ClassFileWriter(name: "jvmbridge/sample/AgentCallbacks").AddNativeMethod(name: "onFrame", descriptor: "(Ljava/lang/Object;)V").ToArray();

        Assert.Equal(HandWrittenCallbackClass, Convert.ToHexStringLower(bytes));
    }

    /// <summary>A written class reads back with its name, hierarchy and native method.</summary>
    [Fact]
    public void WrittenClassesReadBack()
    {
        JavaClassFile file = JavaClassFile.Read(
            new ClassFileWriter(name: "pkg/Helper", superName: "java/lang/Thread", majorVersion: 49).AddNativeMethod(name: "go", descriptor: "()V").ToArray()
        );

        ClassMember method = Assert.Single(file.Methods);

        Assert.Equal(expected: "pkg/Helper", file.Name);
        Assert.Equal(expected: "java/lang/Thread", file.SuperName);
        Assert.Equal(expected: 49, file.MajorVersion);
        Assert.Equal(ClassFileWriter.PublicStaticNative, method.AccessFlags);
        Assert.Null(method.Code);
    }
}

using System.Buffers.Binary;

using JvmBridge.ClassFiles;
using JvmBridge.ClassFiles.Code;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies that <see cref="CodeBuilder.Lambda"/> writes the call site, the constants and the class attributes that javac writes for a method reference, and reuses those a class has.</summary>
public sealed class LambdaTests
{
    private const string Lookup = "java/lang/invoke/MethodHandles$Lookup of java/lang/invoke/MethodHandles Lookup 0x0019";

    private const string Metafactory = "6 java/lang/invoke/LambdaMetafactory.metafactory"
        + "(Ljava/lang/invoke/MethodHandles$Lookup;Ljava/lang/String;Ljava/lang/invoke/MethodType;Ljava/lang/invoke/MethodType;"
        + "Ljava/lang/invoke/MethodHandle;Ljava/lang/invoke/MethodType;)Ljava/lang/invoke/CallSite;";

    /// <summary>Gets the class file versions and implementation flags, with the handle kind javac writes for each.</summary>
    public static TheoryData<int, int, int> HandleKinds { get; } = new()
    {
        { 52, 0x0002, 7 },
        { 58, 0x0002, 7 },
        { 59, 0x0002, 5 },
        { 65, 0x0002, 5 },
        { 52, 0x0001, 5 },
    };

    private static readonly JavaMethod Run = JavaMethod.Interface(owner: "java/lang/Runnable", name: "run", descriptor: "()V");

    private static readonly JavaMethod Accept = JavaMethod.Interface(owner: "java/util/function/Consumer", name: "accept", descriptor: "(Ljava/lang/Object;)V");

    private static readonly JavaMethod Apply = JavaMethod.Interface(owner: "java/util/function/Function", name: "apply", descriptor: "(Ljava/lang/Object;)Ljava/lang/Object;");

    /// <summary>LambdaMetafactory came with Java 8, so a class file older than version 52 is refused, and the editor leaves it as it was.</summary>
    [Fact]
    public void AClassOlderThanJava8IsRefused()
    {
        JavaMethod body = JavaMethod.Static(owner: "test/Old", name: "body", descriptor: "()V");
        byte[] bytes = new ClassFileWriter(name: "test/Old", majorVersion: 51).AddMethod(name: "body", descriptor: "()V", static code => code.Return()).ToArray();
        ClassFileEditor editor = new(bytes);

        InvalidOperationException written = Assert.Throws<InvalidOperationException>(
            () => new ClassFileWriter(name: "test/Old", majorVersion: 51).AddMethod(name: "task", descriptor: "()Ljava/lang/Runnable;", code => code.Lambda(Run, body).AReturn())
        );

        InvalidOperationException edited = Assert.Throws<InvalidOperationException>(() => editor.AssembleCode(code => code.Lambda(Run, body)));

        Assert.Contains(expectedSubstring: "version 52", written.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "version 52", edited.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, editor.ToArray());
    }

    /// <summary>
    /// The edit of the issue that asked for lambdas: the body of <c>clear()</c> moves into a private synthetic method, and <c>clear()</c> hands it to an executor. The class had no class attributes,
    /// so it gets both, and the handle is the one a private method needs at version 52.
    /// </summary>
    [Fact]
    public void AMovedBodyBecomesALambda()
    {
        ClassFileEditor editor = new(
            new ClassFileWriter(name: "test/Channels").AddMethod(name: "clear", descriptor: "()V", static code => code.Return(), accessFlags: 0x0001).ToArray()
        );

        editor.ReplaceMethodBody(name: "clear", descriptor: "()V", movedBodyName: "agent$clear", ClearOnExecutor);

        JavaClassFile file = editor.Model;
        ClassMember moved = file.FindMethod(name: "agent$clear", descriptor: "()V") ?? throw new InvalidOperationException(message: "The body did not move.");

        Assert.Equal(expected: 0x1002, moved.AccessFlags);
        Assert.Equal([Opcodes.Return], moved.Code?.Instructions.Select(static instruction => instruction.Opcode));
        Assert.Equal(expected: 2, file.FindMethod(name: "clear")?.Code?.MaxStack);
        Assert.Equal([$"{Metafactory} ()V | 7 test/Channels.agent$clear()V | ()V"], BootstrapMethods(editor.ToArray()));
        Assert.Equal([Lookup], InnerClasses(editor.ToArray()));
    }

    /// <summary>A different lambda in a class that has some is appended: a second bootstrap method that shares the metafactory handle, and no second InnerClasses entry.</summary>
    [Fact]
    public void ANewLambdaIsAppendedToTheClassAttributes()
    {
        JavaMethod first = JavaMethod.Static(owner: "test/Two", name: "first", descriptor: "()V");
        JavaMethod second = JavaMethod.Static(owner: "test/Two", name: "second", descriptor: "()V");

        byte[] bytes = new ClassFileWriter(name: "test/Two")
            .AddMethod(name: "first", descriptor: "()V", static code => code.Return())
            .AddMethod(name: "second", descriptor: "()V", static code => code.Return())
            .AddMethod(name: "task", descriptor: "()Ljava/lang/Runnable;", code => code.Lambda(Run, first).AReturn())
            .ToArray();

        ClassFileEditor editor = new(bytes);
        byte[] call = editor.AssembleCode(code => code.Lambda(Run, second));
        JavaClassFile file = editor.Model;

        Assert.Equal(
            expected: "1 run ()Ljava/lang/Runnable;",
            CallSite(file, new Instruction(Offset: 0, call[0], Length: 5, BinaryPrimitives.ReadUInt16BigEndian(call.AsSpan(start: 1))))
        );
        Assert.Equal(
            [$"{Metafactory} ()V | 6 test/Two.first()V | ()V", $"{Metafactory} ()V | 6 test/Two.second()V | ()V"],
            BootstrapMethods(editor.ToArray())
        );
        bool IsMetafactory(MethodHandleEntry entry)
        {
            return file.GetMemberReference(entry.ReferenceIndex).Name == "metafactory";
        }

        Assert.Equal(expected: 1, file.ConstantPool.OfType<MethodHandleEntry>().Count(IsMetafactory));
        Assert.Equal([Lookup], InnerClasses(editor.ToArray()));
    }

    /// <summary>javac names a private method with REF_invokeSpecial before class file version 59 and with REF_invokeVirtual from it, and a method that is not private with REF_invokeVirtual always.</summary>
    /// <param name="majorVersion">The class file version.</param>
    /// <param name="accessFlags">The flags of the implementation.</param>
    /// <param name="kind">The handle kind javac writes.</param>
    [Theory]
    [MemberData(nameof(HandleKinds))]
    public void APrivateMethodIsNamedAsJavacNamesIt(int majorVersion, int accessFlags, int kind)
    {
        JavaMethod body = JavaMethod.Virtual(owner: "test/Private", name: "body", descriptor: "()V");

        byte[] bytes = new ClassFileWriter(name: "test/Private", majorVersion: majorVersion)
            .AddMethod(name: "body", descriptor: "()V", static code => code.Return(), accessFlags)
            .AddMethod(name: "task", descriptor: "()Ljava/lang/Runnable;", code => code.ALoad(slot: 0).Lambda(Run, body).AReturn(), accessFlags: 0x0001)
            .ToArray();

        Assert.Equal([$"{Metafactory} ()V | {kind} test/Private.body()V | ()V"], BootstrapMethods(bytes));
    }

    /// <summary>A class written with a lambda gets the call site, one bootstrap method with the erased type, the handle and the instantiated type, and the InnerClasses entry of <c>MethodHandles$Lookup</c>.</summary>
    [Fact]
    public void AWrittenClassGetsTheCallSiteAndItsAttributes()
    {
        JavaMethod print = JavaMethod.Static(owner: "test/Writer", name: "print", descriptor: "(Ljava/lang/String;)V");

        byte[] bytes = new ClassFileWriter(name: "test/Writer")
            .AddMethod(name: "print", print.Descriptor, static code => code.Return(), accessFlags: 0x000a)
            .AddMethod(name: "printer", descriptor: "()Ljava/util/function/Consumer;", code => code.Lambda(Accept, print).AReturn())
            .ToArray();

        JavaClassFile file = JavaClassFile.Read(bytes);
        CodeAttribute code = file.FindMethod(name: "printer")?.Code ?? throw new InvalidOperationException(message: "printer has no code.");

        Assert.Equal([Opcodes.InvokeDynamic, Opcodes.AReturn], code.Instructions.Select(static instruction => instruction.Opcode));
        Assert.Equal(expected: 1, code.MaxStack);
        Assert.Equal(expected: "0 accept ()Ljava/util/function/Consumer;", CallSite(file, code.Instructions[index: 0]));
        Assert.Equal(
            [$"{Metafactory} (Ljava/lang/Object;)V | 6 test/Writer.print(Ljava/lang/String;)V | (Ljava/lang/String;)V"],
            BootstrapMethods(bytes)
        );
        Assert.Equal([Lookup], InnerClasses(bytes));
    }

    /// <summary>A lambda the class has already is reused whole: the same call site, with no new constant or bootstrap method.</summary>
    [Fact]
    public void AnExistingLambdaIsReused()
    {
        ClassFileEditor editor = new(
            new ClassFileWriter(name: "test/Channels").AddMethod(name: "clear", descriptor: "()V", static code => code.Return(), accessFlags: 0x0001).ToArray()
        );

        editor.ReplaceMethodBody(name: "clear", descriptor: "()V", movedBodyName: "agent$clear", ClearOnExecutor);

        byte[] before = editor.ToArray();
        CodeAttribute clear = editor.Model.FindMethod(name: "clear")?.Code ?? throw new InvalidOperationException(message: "clear has no code.");
        byte[] again = editor.AssembleCode(static code => code.Lambda(Run, JavaMethod.Virtual(owner: "test/Channels", name: "agent$clear", descriptor: "()V")));

        Assert.Equal(before, editor.ToArray());
        Assert.Equal(before.AsSpan(clear.CodeOffset + clear.Instructions[index: 1].Offset, length: 5).ToArray(), again);
    }

    /// <summary>The receiver and the parameters that the interface method does not pass are captured; a primitive the implementation takes where the interface passes an object is boxed in the instantiated type.</summary>
    [Fact]
    public void LeadingValuesAreCapturedAndPrimitivesBoxed()
    {
        JavaMethod format = JavaMethod.Virtual(owner: "test/Capture", name: "format", descriptor: "(Ljava/lang/String;I)Ljava/lang/String;");

        byte[] bytes = new ClassFileWriter(name: "test/Capture")
            .AddMethod(
                name: "formatter",
                descriptor: "()Ljava/util/function/Function;",
                code => code.ALoad(slot: 0).Ldc(value: "x").Lambda(Apply, format).AReturn(),
                accessFlags: 0x0001
            )
            .ToArray();

        JavaClassFile file = JavaClassFile.Read(bytes);
        CodeAttribute code = file.FindMethod(name: "formatter")?.Code ?? throw new InvalidOperationException(message: "formatter has no code.");

        Assert.Equal(expected: 2, code.MaxStack);
        Assert.Equal(expected: "0 apply (Ltest/Capture;Ljava/lang/String;)Ljava/util/function/Function;", CallSite(file, code.Instructions[index: 2]));
        Assert.Equal(
            [$"{Metafactory} (Ljava/lang/Object;)Ljava/lang/Object; | 5 test/Capture.format(Ljava/lang/String;I)Ljava/lang/String; | (Ljava/lang/Integer;)Ljava/lang/String;"],
            BootstrapMethods(bytes)
        );
    }

    /// <summary>Methods that cannot make a lambda are refused before anything is added: an interface method not described as one, an implementation that takes too few values, and one that returns nothing where an object is due.</summary>
    [Fact]
    public void MismatchedMethodsAreRefused()
    {
        JavaMethod noArguments = JavaMethod.Static(owner: "test/Bad", name: "none", descriptor: "()V");
        ClassFileWriter writer = new(name: "test/Bad");

        JavaMethod use = JavaMethod.Static(owner: "test/Bad", name: "use", descriptor: "(Ljava/lang/Object;)V");
        JavaMethod notInterface = JavaMethod.Virtual(Run.Owner, Run.Name, Run.Descriptor);

        ArgumentException virtualCall = Refused(writer, code => code.Lambda(notInterface, noArguments));
        ArgumentException tooFew = Refused(writer, code => code.Lambda(Accept, noArguments));
        ArgumentException noResult = Refused(writer, code => code.Lambda(Apply, use));

        Assert.Contains(expectedSubstring: "JavaMethod.Interface", virtualCall.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "fewer than the 1 parameters", tooFew.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "returns nothing", noResult.Message, StringComparison.Ordinal);
    }

    private static AttributeData Attribute(byte[] bytes, JavaClassFile file, string name)
    {
        ClassAttributeRange range = file.Attributes.Single(attribute => attribute.Name == name);
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(range.DataStart));

        return new AttributeData(bytes.AsMemory(range.DataStart, range.End - range.DataStart), count);
    }

    private static List<string> BootstrapMethods(byte[] bytes)
    {
        JavaClassFile file = JavaClassFile.Read(bytes);
        (ReadOnlyMemory<byte> data, int count) = Attribute(bytes, file, name: "BootstrapMethods");
        List<string> methods = [];
        int at = 2;

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = data.Span[at..];
            int arguments = BinaryPrimitives.ReadUInt16BigEndian(entry[2..]);
            IEnumerable<string> described = Enumerable.Range(start: 0, arguments).Select(argument => Describe(file, BinaryPrimitives.ReadUInt16BigEndian(data.Span[(at + 4 + (2 * argument))..])));

            methods.Add($"{Describe(file, BinaryPrimitives.ReadUInt16BigEndian(entry))} {string.Join(separator: " | ", described)}");
            at += 4 + (2 * arguments);
        }

        return methods;
    }

    private static string CallSite(JavaClassFile file, Instruction instruction)
    {
        DynamicEntry entry = (DynamicEntry)(file.ConstantPool[instruction.ConstantIndex ?? 0] ?? throw new InvalidOperationException(message: "The instruction names no constant."));
        NameAndTypeEntry signature = (NameAndTypeEntry)(file.ConstantPool[entry.NameAndTypeIndex] ?? throw new InvalidOperationException(message: "The call site has no signature."));

        return $"{entry.BootstrapMethodIndex} {file.GetUtf8(signature.NameIndex)} {file.GetUtf8(signature.DescriptorIndex)}";
    }

    private static CodeBuilder ClearOnExecutor(CodeBuilder code)
    {
        return code.ALoad(slot: 0)
            .Lambda(Run, JavaMethod.Virtual(owner: "test/Channels", name: "agent$clear", descriptor: "()V"))
            .AConstNull()
            .Invoke(
                JavaMethod.Static(
                    owner: "java/util/concurrent/CompletableFuture",
                    name: "runAsync",
                    descriptor: "(Ljava/lang/Runnable;Ljava/util/concurrent/Executor;)Ljava/util/concurrent/CompletableFuture;"
                )
            )
            .Invoke(JavaMethod.Virtual(owner: "java/util/concurrent/CompletableFuture", name: "join", descriptor: "()Ljava/lang/Object;"))
            .Pop()
            .Return();
    }

    private static string Describe(JavaClassFile file, int index)
    {
        return file.ConstantPool[index] switch
        {
            MethodHandleEntry handle => $"{handle.ReferenceKind} {Member(file.GetMemberReference(handle.ReferenceIndex))}",
            MethodTypeEntry type => file.GetUtf8(type.DescriptorIndex),
            _ => $"#{index}",
        };
    }

    private static List<string> InnerClasses(byte[] bytes)
    {
        JavaClassFile file = JavaClassFile.Read(bytes);
        (ReadOnlyMemory<byte> data, int count) = Attribute(bytes, file, name: "InnerClasses");

        return [.. Enumerable.Range(start: 0, count).Select(
            index =>
            {
            ReadOnlySpan<byte> entry = data.Span[(2 + (8 * index))..];

            return $"{file.GetClassName(BinaryPrimitives.ReadUInt16BigEndian(entry))} of {file.GetClassName(BinaryPrimitives.ReadUInt16BigEndian(entry[2..]))} {file.GetUtf8(BinaryPrimitives.ReadUInt16BigEndian(entry[4..]))} 0x{BinaryPrimitives.ReadUInt16BigEndian(entry[6..]):x4}";
        }
        )];
    }

    private static string Member(MemberReference reference)
    {
        return $"{reference.Owner}.{reference.Name}{reference.Descriptor}";
    }

    private static ArgumentException Refused(ClassFileWriter writer, Func<CodeBuilder, CodeBuilder> lambda)
    {
        return Assert.Throws<ArgumentException>(() => writer.AddMethod(name: "make", descriptor: "()Ljava/lang/Object;", code => lambda(code).AReturn()));
    }

    /// <summary>The data of a class attribute, from its entry count on, and that count.</summary>
    private readonly record struct AttributeData(ReadOnlyMemory<byte> Data, int Count);
}

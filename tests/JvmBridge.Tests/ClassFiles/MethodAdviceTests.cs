using System.Text.RegularExpressions;

using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies method advice on class bytes, without a JVM: what the stub looks like, what moves, and why advice is refused.</summary>
public sealed class MethodAdviceTests
{
    private const string Self = "Ljava/lang/Object;";

    private static JavaMethod After => JavaMethod.Static(owner: "pkg/Hooks", name: "after", $"({Self}I)I");
    private static JavaMethod Before => JavaMethod.Static(owner: "pkg/Hooks", name: "before", $"({Self}Ljava/lang/String;)V");

    /// <summary>The method keeps its descriptor and flags, its code moves unchanged into a private synthetic static method, and the stub calls the before hook, the body and the after hook in that order.</summary>
    [Fact]
    public void AStaticMethodGetsAStubAndABody()
    {
        byte[] original = ClassFileFixtures.Sample;

        ClassPatchResult advised = ClassPatch.For(className: "fixture/Sample")
            .Method(name: "length", descriptor: "(Ljava/lang/String;)I")
            .Before(Before)
            .After(After)
            .Apply(original);

        Assert.True(advised.Changed, advised.SkippedReason);
        Assert.Equal(expected: 1, advised.AdvisedMethods);

        JavaClassFile before = JavaClassFile.Read(original);
        JavaClassFile after = JavaClassFile.Read(advised.Bytes ?? []);
        ClassMember stub = after.GetMethod(name: "length");
        ClassMember body = after.GetMethod(name: "length$jvmbridge$body");

        Assert.Equal(before.Methods.Count + 1, after.Methods.Count);
        Assert.Equal(before.GetMethod(name: "length").AccessFlags, stub.AccessFlags);
        Assert.Equal(expected: 0x100A, body.AccessFlags);
        Assert.Equal(before.GetMethod(name: "length").Descriptor, body.Descriptor);

        Assert.Equal(before.GetCode(name: "length").Instructions.Select(static instruction => instruction.Opcode),
            body.Code?.Instructions.Select(static instruction => instruction.Opcode));

        Assert.Equal(expected: """
              method public static length(Ljava/lang/String;)I
                max_stack=2 max_locals=2
                  0: aconst_null
                  1: aload_0
                  2: invokestatic #__ // Method pkg/Hooks.before:(Ljava/lang/Object;Ljava/lang/String;)V
                  5: aload_0
                  6: invokestatic #__ // Method fixture/Sample.length$jvmbridge$body:(Ljava/lang/String;)I
                  9: istore_1
                  10: aconst_null
                  11: iload_1
                  12: invokestatic #__ // Method pkg/Hooks.after:(Ljava/lang/Object;I)I
                  15: ireturn

            """,
            Normalize(ClassFileDisassembler.Disassemble(advised.Bytes ?? [], method: "length")));
    }

    /// <summary>Advising a method that is advised already is refused, so applying a patch to its own output does nothing.</summary>
    [Fact]
    public void AdviceIsAppliedOnce()
    {
        ClassPatch patch = ClassPatch.For(className: "fixture/Sample")
            .Method(name: "length", descriptor: "(Ljava/lang/String;)I")
            .Before(Before);

        ClassPatchResult first = patch.Apply(ClassFileFixtures.Sample);
        ClassPatchResult second = patch.Apply(first.Bytes ?? []);

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Equal(PatchSkipCategory.AlreadyAdvised, second.SkipCategory);
    }

    /// <summary>An instance method passes <c>this</c>, calls its body with <c>invokespecial</c>, and a long or double result takes two slots.</summary>
    [Fact]
    public void AnInstanceMethodPassesThisAndKeepsWideResultsApart()
    {
        byte[] bytes = new ClassFileWriter(name: "pkg/Subject").AddMethod(name: "wide", descriptor: "(JI)J", maxStack: 2, maxLocals: 4, code: [0x1e, 0xad], accessFlags: 0x0001)
            .ToArray();

        JavaMethod hook = JavaMethod.Static(owner: "pkg/Hooks", name: "after", $"({Self}JIJ)J");

        ClassPatchResult advised = ClassPatch.For(className: "pkg/Subject")
            .Method(name: "wide", descriptor: "(JI)J")
            .After(hook, includeArguments: true)
            .Apply(bytes);

        Assert.True(advised.Changed, advised.SkippedReason);

        Assert.Equal(expected: """
              method public wide(JI)J
                max_stack=6 max_locals=6
                  0: aload_0
                  1: lload_1
                  2: iload_3
                  3: invokespecial #__ // Method pkg/Subject.wide$jvmbridge$body:(JI)J
                  6: lstore 4
                  8: aload_0
                  9: lload_1
                  10: iload_3
                  11: lload 4
                  13: invokestatic #__ // Method pkg/Hooks.after:(Ljava/lang/Object;JIJ)J
                  16: lreturn

            """,
            Normalize(ClassFileDisassembler.Disassemble(advised.Bytes ?? [], method: "wide")));
    }

    /// <summary>Advice needs a method and a descriptor, and each hook can be given once per method.</summary>
    [Fact]
    public void ChecksHowAdviceIsDeclared()
    {
        ClassPatch patch = ClassPatch.For(className: "fixture/Sample");

        // Advice without a method or without its descriptor, and a second hook of either kind on one method.
        InvalidOperationException noMethod = Assert.Throws<InvalidOperationException>(() => patch.Before(Before));

        InvalidOperationException noDescriptor = Assert.Throws<InvalidOperationException>(() => patch.Method(name: "length")
            .Before(Before));

        InvalidOperationException twoBefore = Assert.Throws<InvalidOperationException>(() => patch.Method(name: "length", descriptor: "(Ljava/lang/String;)I")
            .Before(Before)
            .Before(Before));

        InvalidOperationException twoAfter = Assert.Throws<InvalidOperationException>(() => patch.Method(name: "length", descriptor: "(Ljava/lang/String;)I")
            .After(After)
            .After(After));

        Assert.Contains(expectedSubstring: "needs a method and its descriptor", noMethod.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "needs a method and its descriptor", noDescriptor.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "already has a Before hook", twoBefore.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "already has an After hook", twoAfter.Message, StringComparison.Ordinal);
    }

    /// <summary>Edits of a patch are applied before advice, so a redirect scoped to the advised method still sees its original code.</summary>
    [Fact]
    public void EditsSeeTheOriginalCodeBeforeAdvice()
    {
        ClassPatch patch = ClassPatch.For(className: "fixture/Sample")
            .Method(name: "length", descriptor: "(Ljava/lang/String;)I")
            .Before(Before)
            .RedirectInvocation(JavaMethod.Virtual(owner: "java/lang/String", name: "length", descriptor: "()I"), toOwner: "pkg/Strings");

        ClassPatchResult applied = patch.Apply(ClassFileFixtures.Sample);

        Assert.True(applied.Changed, applied.SkippedReason);
        Assert.Equal(expected: 1, applied.RedirectedInvocations);
        Assert.Equal(expected: 1, applied.AdvisedMethods);

        Assert.Contains(expectedSubstring: "pkg/Strings.length",
            ClassFileDisassembler.Disassemble(applied.Bytes ?? [], method: "length$jvmbridge$body"),
            StringComparison.Ordinal);
    }

    /// <summary>A method with more parameters than the stub's one-byte slot numbers can address is refused up front.</summary>
    [Fact]
    public void RefusesMethodsWithTooManyParameters()
    {
        string parameters = string.Concat(Enumerable.Repeat(element: "J", count: 130));
        string descriptor = $"({parameters})V";

        byte[] bytes = new ClassFileWriter(name: "pkg/Subject").AddMethod(name: "many", descriptor, maxStack: 0, maxLocals: 260, code: [0xb1])
            .ToArray();

        JavaMethod hook = JavaMethod.Static(owner: "pkg/Hooks", name: "before", $"({Self}{parameters})V");

        Assert.Equal(PatchSkipCategory.TooManySlots, Refuse(bytes, className: "pkg/Subject", name: "many", descriptor, hook));
    }

    /// <summary>Advice that the class cannot take leaves the class unchanged, and the result says which kind of reason it was.</summary>
    [Fact]
    public void RefusesWhatItCannotAdvise()
    {
        byte[] plain = new ClassFileWriter(name: "pkg/Subject").AddMethod(name: "run", descriptor: "()V", maxStack: 0, maxLocals: 1, code: [0xb1], accessFlags: 0x0001)
            .AddMethod(name: "<init>", descriptor: "()V", maxStack: 0, maxLocals: 1, code: [0xb1], accessFlags: 0x0001)
            .AddMethod(name: "bridge", descriptor: "()V", maxStack: 0, maxLocals: 1, code: [0xb1], accessFlags: 0x1041)
            .AddNativeMethod(name: "nativeMethod", descriptor: "()V", accessFlags: 0x0101)
            .ToArray();

        byte[] contract = new ClassFileWriter(name: "pkg/Contract", accessFlags: 0x0601).AddMethod(name: "make", descriptor: "()V", maxStack: 0, maxLocals: 0, code: [0xb1])
            .ToArray();

        JavaMethod hook = JavaMethod.Static(owner: "pkg/Hooks", name: "before", $"({Self})V");

        // A method that is not there, or that is not ordinary code of the class, cannot be advised.
        Assert.Equal(PatchSkipCategory.MethodNotFound,
            Refuse(plain, className: "pkg/Subject", name: "missing", descriptor: "()V", hook));

        Assert.Equal(PatchSkipCategory.InitializerNotSupported,
            Refuse(plain, className: "pkg/Subject", name: "<init>", descriptor: "()V", hook));

        Assert.Equal(PatchSkipCategory.BridgeOrSyntheticMethod,
            Refuse(plain, className: "pkg/Subject", name: "bridge", descriptor: "()V", hook));

        Assert.Equal(PatchSkipCategory.NativeOrAbstractMethod,
            Refuse(plain, className: "pkg/Subject", name: "nativeMethod", descriptor: "()V", hook));

        // Nor can any method of an interface.
        Assert.Equal(PatchSkipCategory.InterfaceClass,
            Refuse(contract, className: "pkg/Contract", name: "make", descriptor: "()V", hook));

        // A hook that does not take the receiver, or that is not static, has the wrong shape for the method.
        Assert.Equal(PatchSkipCategory.HookShapeMismatch,
            Refuse(plain,
                className: "pkg/Subject",
                name: "run",
                descriptor: "()V",
                JavaMethod.Static(owner: "pkg/Hooks", name: "before", descriptor: "()V")));

        Assert.Equal(PatchSkipCategory.HookShapeMismatch,
            Refuse(plain,
                className: "pkg/Subject",
                name: "run",
                descriptor: "()V",
                JavaMethod.Virtual(owner: "pkg/Hooks", name: "before", $"({Self})V")));
    }

    private static string Normalize(string disassembly)
    {
        return Regex.Replace(disassembly, pattern: "#\\d+", replacement: "#__");
    }

    private static PatchSkipCategory Refuse(byte[] bytes, string className, string name, string descriptor, JavaMethod hook)
    {
        ClassPatchResult refused = ClassPatch.For(className).Method(name, descriptor).Before(hook).Apply(bytes);

        Assert.False(refused.Changed);

        return refused.SkipCategory;
    }
}

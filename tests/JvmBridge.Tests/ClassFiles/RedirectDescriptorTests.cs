using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies redirects to a helper with a descriptor of its own, and edits that report what they did, without a JVM.</summary>
public sealed class RedirectDescriptorTests
{
    private const string Self = "Ljava/lang/Object;";

    private static readonly JavaMethod StringLength = JavaMethod.Virtual(owner: "java/lang/String",
        name: "length",
        descriptor: "()I");

    private static readonly JavaMethod ListSize = JavaMethod.Interface(owner: "java/util/List", name: "size", descriptor: "()I");

    private static ClassPatch Sample => ClassPatch.For(className: "fixture/Sample");

    /// <summary>The descriptor of the call is kept where the helper declares it exactly, and an interface receiver can go to Object too.</summary>
    [Fact]
    public void AcceptsTheExactTypesAndInterfaceReceivers()
    {
        ClassPatchResult exact = Sample.RedirectInvocation(StringLength, JavaMethod.Static(owner: "pkg/Helpers", name: "measure", descriptor: "(Ljava/lang/String;)I"))
            .Apply(ClassFileFixtures.Sample);

        ClassPatchResult receiver = Sample.RedirectInvocation(ListSize, JavaMethod.Static(owner: "pkg/Helpers", name: "count", descriptor: $"({Self})I"))
            .Apply(ClassFileFixtures.Sample);

        Assert.True(exact.Changed, exact.SkippedReason);
        Assert.True(receiver.Changed, receiver.SkippedReason);

        Assert.Contains(expectedSubstring: "pkg/Helpers.count:(Ljava/lang/Object;)I",
            ClassFileDisassembler.Disassemble(receiver.Bytes ?? [], method: "size"),
            StringComparison.Ordinal);
    }

    /// <summary>The receiver of a virtual call can go to a helper that takes Object, and the result and log say so as for any redirect.</summary>
    [Fact]
    public void RedirectsToAHelperThatTakesObject()
    {
        ClassPatchResult redirected = Sample.Method(name: "length", descriptor: "(Ljava/lang/String;)I")
            .RedirectInvocation(StringLength, JavaMethod.Static(owner: "pkg/Helpers", name: "measure", descriptor: $"({Self})I"))
            .Apply(ClassFileFixtures.Sample);

        Assert.True(redirected.Changed, redirected.SkippedReason);
        Assert.Equal(expected: 1, redirected.RedirectedInvocations);

        Assert.Equal(expected: "patched fixture/Sample (length(Ljava/lang/String;)I: 1 invocation of java/lang/String.length redirected)",
            redirected.ToString());

        Assert.Contains(expectedSubstring: "invokestatic #",
            ClassFileDisassembler.Disassemble(redirected.Bytes ?? [], method: "length"),
            StringComparison.Ordinal);

        Assert.Contains(expectedSubstring: "// Method pkg/Helpers.measure:(Ljava/lang/Object;)I",
            ClassFileDisassembler.Disassemble(redirected.Bytes ?? [], method: "length"),
            StringComparison.Ordinal);
    }

    /// <summary>A target that the operands of the call cannot be passed to is refused when the patch is built, with the reason.</summary>
    [Fact]
    public void RefusesTargetsTheCallCannotReach()
    {
        JavaMethod sort = JavaMethod.Static(owner: "java/util/Collections",
            name: "sort",
            descriptor: "(Ljava/util/List;Ljava/util/Comparator;)V");

        ArgumentException count = Assert.Throws<ArgumentException>(() => Sample.RedirectInvocation(StringLength,
            JavaMethod.Static(owner: "pkg/Helpers", name: "measure", descriptor: "()I")));

        ArgumentException returnType = Assert.Throws<ArgumentException>(() => Sample.RedirectInvocation(StringLength,
            JavaMethod.Static(owner: "pkg/Helpers", name: "measure", descriptor: $"({Self})J")));

        ArgumentException other = Assert.Throws<ArgumentException>(() => Sample.RedirectInvocation(StringLength,
            JavaMethod.Static(owner: "pkg/Helpers", name: "measure", descriptor: "(Ljava/lang/CharSequence;)I")));

        ArgumentException primitive = Assert.Throws<ArgumentException>(() => Sample.RedirectInvocation(sort,
            JavaMethod.Static(owner: "pkg/Helpers", name: "sort", descriptor: $"({Self}I)V")));

        ArgumentException kind = Assert.Throws<ArgumentException>(() => Sample.RedirectInvocation(StringLength,
            JavaMethod.Virtual(owner: "pkg/Helpers", name: "measure", descriptor: $"({Self})I")));

        // A target that takes a different number of operands, or returns another type, is refused with both shapes.
        Assert.Contains(expectedSubstring: "passes 1 operand ((Ljava/lang/String;)I), the target takes 0",
            count.Message,
            StringComparison.Ordinal);

        Assert.Contains(expectedSubstring: "the call returns I, the target J", returnType.Message, StringComparison.Ordinal);

        // An operand that a parameter of the target cannot hold is named with its position.
        Assert.Contains(expectedSubstring: "operand 1 is Ljava/lang/String;", other.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "operand 2 is Ljava/util/Comparator;", primitive.Message, StringComparison.Ordinal);

        // A target that is not static is refused whatever it takes.
        Assert.Contains(expectedSubstring: "must be static", kind.Message, StringComparison.Ordinal);
    }

    /// <summary>A reporting edit is counted and logged like a built-in edit, gets the scope, and skips the class when it changes nothing or its method is missing.</summary>
    [Fact]
    public void ReportingEditsCountScopeAndSkip()
    {
        string? scopedName = null;
        string? scopedDescriptor = null;

        ClassPatchResult applied = Sample.Method(name: "length", descriptor: "(Ljava/lang/String;)I")
            .Edit(description: "string length calls measured",
                context =>
                {
                    scopedName = context.MethodName;
                    scopedDescriptor = context.MethodDescriptor;

                    return context.Editor
                        .ReplaceInvocation(StringLength, toOwner: "pkg/Helpers", methodName: context.MethodName, methodDescriptor: context.MethodDescriptor);
                })
            .Apply(ClassFileFixtures.Sample);

        ClassPatchResult nothing = Sample.Method(name: "limit", descriptor: "()I")
            .Edit(description: "string length calls measured",
                context => context.Editor
                    .ReplaceInvocation(StringLength, toOwner: "pkg/Helpers", methodName: context.MethodName, methodDescriptor: context.MethodDescriptor))
            .Apply(ClassFileFixtures.Sample);

        ClassPatchResult missing = Sample.Method(name: "absent", descriptor: "()V")
            .Edit(description: "never runs", static context => throw new InvalidOperationException(message: "ran"))
            .Apply(ClassFileFixtures.Sample);

        ClassPatchResult failing = Sample.Edit(description: "fails", static context => throw new ClassFormatException(message: "boom"))
            .Apply(ClassFileFixtures.Sample);

        // The edit ran with the method scope it was declared in.
        Assert.Equal(expected: "length", scopedName);
        Assert.Equal(expected: "(Ljava/lang/String;)I", scopedDescriptor);

        // It counts as one edit and is logged with its description and the number of changes.
        Assert.True(applied.Changed, applied.SkippedReason);
        Assert.Equal(expected: 1, applied.AppliedEdits);

        Assert.Equal(expected: "patched fixture/Sample (length(Ljava/lang/String;)I: string length calls measured (1 change))",
            applied.ToString());

        // An edit that changes nothing, a scope whose method is missing and an edit that throws each skip the class with the reason.
        Assert.Equal(expected: "string length calls measured: nothing to change in limit()I", nothing.SkippedReason);
        Assert.Equal(expected: "method absent()V not found", missing.SkippedReason);
        Assert.Equal(expected: "an edit failed: boom", failing.SkippedReason);
    }
}

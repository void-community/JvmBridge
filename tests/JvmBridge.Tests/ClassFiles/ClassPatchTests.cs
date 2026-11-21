using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies declarative class patches without a JVM.</summary>
public sealed class ClassPatchTests
{
    private const string SortDescriptor = "(Ljava/util/List;Ljava/util/Comparator;)V";

    private static readonly JavaMethod CollectionsSort = JavaMethod.Static(owner: "java/util/Collections",
        name: "sort",
        SortDescriptor);

    private static ClassPatch Sample => ClassPatch.For(className: "fixture/Sample");

    /// <summary>Access flags change for the class, a scoped method or a field.</summary>
    [Fact]
    public void ChangesAccessFlags()
    {
        ClassPatch patch = Sample.SetAccess(accessFlags: 0x0001)
            .Method(name: "count", descriptor: "()I")
            .SetAccess(accessFlags: 0x0011)
            .AnyMethod()
            .SetFieldAccess(name: "count", accessFlags: 0x0001);

        ClassPatchResult applied = patch.Apply(ClassFileFixtures.Sample);
        ClassPatchResult noField = Sample.SetFieldAccess(name: "absent", accessFlags: 0x0001).Apply(ClassFileFixtures.Sample);
        JavaClassFile patched = JavaClassFile.Read(applied.Bytes ?? []);

        // The class, the method and the field each count as one change and have their new flags.
        Assert.Equal(expected: 3, applied.ChangedAccessFlags);
        Assert.Equal(expected: 0x0001, patched.AccessFlags);
        Assert.Equal(expected: 0x0011, patched.GetMethod(name: "count").AccessFlags);
        Assert.Equal(expected: 0x0001, patched.GetField(name: "count").AccessFlags);

        // A field the class does not have skips the patch, with the reason.
        Assert.Equal(expected: "field absent not found", noField.SkippedReason);
    }

    /// <summary>The escape hatch runs a custom edit, and its failure skips the patch instead of throwing.</summary>
    [Fact]
    public void CustomEditsRunAndTheirFailuresAreReported()
    {
        int index = 0;
        ClassPatch patch = Sample.Edit(editor => index = editor.AddUtf8(value: "custom"));
        ClassPatch failing = Sample.Edit(editor => throw new ClassFormatException(message: "boom"));

        // Both patches meet the same class, one with an edit that works and one with an edit that throws.
        ClassPatchResult applied = patch.Apply(ClassFileFixtures.Sample);
        ClassPatchResult failure = failing.Apply(ClassFileFixtures.Sample);

        Assert.Equal(expected: 1, applied.AppliedEdits);
        Assert.True(index > 0);
        Assert.Equal(expected: "an edit failed: boom", failure.SkippedReason);
    }

    /// <summary>A patch whose method or call is missing leaves the class unchanged and says why.</summary>
    [Fact]
    public void MissingTargetsLeaveTheClassUnchanged()
    {
        ClassPatchResult noMethod = Sample.Method(name: "absent", descriptor: "()V")
            .RedirectInvocation(CollectionsSort, toOwner: "fixture/Helper")
            .Apply(ClassFileFixtures.Sample);

        ClassPatchResult noCall = Sample.Method(name: "greeting")
            .RedirectInvocation(CollectionsSort, toOwner: "fixture/Helper")
            .Apply(ClassFileFixtures.Sample);

        // A method the class does not have leaves no bytes, and the reason and the log line both say which method.
        Assert.False(noMethod.Changed);
        Assert.Null(noMethod.Bytes);
        Assert.Equal(expected: "method absent()V not found", noMethod.SkippedReason);
        Assert.Equal(expected: "skipped fixture/Sample: method absent()V not found", noMethod.ToString());

        // A call the method does not make is reported with the call and the method that was searched.
        Assert.Contains(expectedSubstring: "no invocation of java/util/Collections.sort",
            noCall.SkippedReason,
            StringComparison.Ordinal);

        Assert.Contains(expectedSubstring: "in greeting", noCall.SkippedReason, StringComparison.Ordinal);
    }

    /// <summary>One missing target skips the whole patch, so a class is never half patched.</summary>
    [Fact]
    public void PatchesAreAllOrNothing()
    {
        ClassPatch patch = Sample.RedirectInvocation(CollectionsSort, toOwner: "fixture/Helper")
            .ReplaceConstant(oldValue: "absent", newValue: "present");

        ClassPatchResult skipped = patch.Apply(ClassFileFixtures.Sample);

        Assert.False(skipped.Changed);
        Assert.Null(skipped.Bytes);
        Assert.Equal(expected: 0, skipped.RedirectedInvocations);
        Assert.Contains(expectedSubstring: "absent", skipped.SkippedReason, StringComparison.Ordinal);
    }

    /// <summary>Patches are immutable, so adding an edit never changes the patch it started from.</summary>
    [Fact]
    public void PatchesAreImmutableAndReusable()
    {
        ClassPatch empty = Sample;
        ClassPatch redirect = empty.RedirectInvocation(CollectionsSort, toOwner: "fixture/Helper");
        byte[] input = ClassFileFixtures.Sample;

        // The redirect is applied twice to the same bytes, and the patch it was built from once.
        ClassPatchResult first = redirect.Apply(input);
        ClassPatchResult second = redirect.Apply(input);
        ClassPatchResult nothing = empty.Apply(input);

        // Applying a patch again gives the same bytes and leaves its input alone.
        Assert.True(first.Changed);
        Assert.Equal(first.Bytes, second.Bytes);
        Assert.Equal(ClassFileFixtures.Sample, input);

        // The patch that the redirect started from still has no edits.
        Assert.False(nothing.Changed);
        Assert.Equal(expected: "the patch has no edits", nothing.SkippedReason);
    }

    /// <summary>A scoped redirect reports exactly what it changed, in the form an agent logs.</summary>
    [Fact]
    public void RedirectsAnInvocationInOneMethodAndReportsIt()
    {
        ClassPatch patch = Sample.Method(name: "sortOnce", SortDescriptor)
            .RedirectInvocation(CollectionsSort, toOwner: "fixture/Helper");

        ClassPatchResult applied = patch.Apply(ClassFileFixtures.Sample);

        // One call was redirected and nothing was skipped.
        Assert.True(applied.Changed);
        Assert.Equal(expected: 1, applied.RedirectedInvocations);
        Assert.Null(applied.SkippedReason);

        // The detail and the line an agent logs both name the method and the call.
        Assert.Equal(expected: $"sortOnce{SortDescriptor}: 1 invocation of java/util/Collections.sort redirected",
            Assert.Single(applied.Details));

        Assert.Equal(expected: $"patched fixture/Sample (sortOnce{SortDescriptor}: 1 invocation of java/util/Collections.sort redirected)",
            applied.ToString());

        JavaClassFile patched = JavaClassFile.Read(applied.Bytes ?? []);

        Assert.Equal(expected: "fixture/Helper", OwnerOfFirstCall(patched, method: "sortOnce"));
        Assert.Equal(expected: "java/util/Collections", OwnerOfFirstCall(patched, method: "sortTwice"));
    }

    /// <summary>Without a method scope every matching call in the class is redirected.</summary>
    [Fact]
    public void RedirectsEveryMatchingInvocationWithoutAScope()
    {
        ClassPatchResult applied = Sample.RedirectInvocation(CollectionsSort, toOwner: "fixture/Helper", toName: "stableSort")
            .Apply(ClassFileFixtures.Sample);

        Assert.Equal(expected: 3, applied.RedirectedInvocations);
        Assert.Equal(expected: "3 invocations of java/util/Collections.sort redirected", Assert.Single(applied.Details));
    }

    /// <summary>A redirect can name an interface as the owner of its replacement.</summary>
    [Fact]
    public void RedirectsToAnInterfaceOwner()
    {
        ClassPatch patch = Sample.RedirectInvocation(JavaMethod.Virtual(owner: "java/lang/String",
                name: "length",
                descriptor: "()I"),
            toOwner: "fixture/IHelper",
            ownerIsInterface: true);

        ClassPatchResult applied = patch.Apply(ClassFileFixtures.Sample);
        JavaClassFile patched = JavaClassFile.Read(applied.Bytes ?? []);

        Instruction call = patched.GetCode(name: "length")
            .Instructions
            .Single(static instruction => instruction.Opcode is Opcodes.InvokeStatic);

        Assert.Equal(ConstantTag.InterfaceMethodref, patched.GetMemberReference(call.ConstantIndex ?? 0).Kind);
    }

    /// <summary>Class names use slashes.</summary>
    [Fact]
    public void RejectsDottedClassNames()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(static () => ClassPatch.For(className: "fixture.Sample"));

        Assert.Equal(expected: "className", failure.ParamName);
    }

    /// <summary>Input that is the wrong class, or not a class at all, is reported rather than thrown.</summary>
    [Fact]
    public void RejectsTheWrongClassAndInvalidBytes()
    {
        ClassPatch patch = ClassPatch.For(className: "other/Name")
            .RedirectInvocation(CollectionsSort, toOwner: "fixture/Helper");

        ClassPatchResult wrongClass = patch.Apply(ClassFileFixtures.Sample);
        ClassPatchResult notAClass = patch.Apply(classBytes: [1, 2, 3]);

        Assert.Equal(expected: "the class file is fixture/Sample", wrongClass.SkippedReason);
        Assert.StartsWith(expectedStartString: "not a valid class file", notAClass.SkippedReason, StringComparison.Ordinal);
    }

    /// <summary>Every constant type can be replaced, and a missing constant skips the patch.</summary>
    [Fact]
    public void ReplacesConstants()
    {
        ClassPatch patch = Sample.ReplaceConstant(oldValue: "hello", newValue: "goodbye")
            .ReplaceConstant(oldValue: 100000, newValue: 7)
            .ReplaceConstant(oldValue: 5000000000L, newValue: 9L)
            .ReplaceConstant(oldValue: 2.5f, newValue: 0.5f)
            .ReplaceConstant(oldValue: 3.5, newValue: 0.25);

        ClassPatchResult applied = patch.Apply(ClassFileFixtures.Sample);
        ClassPatchResult missing = Sample.ReplaceConstant(oldValue: 12345, newValue: 1).Apply(ClassFileFixtures.Sample);

        Assert.Equal(expected: 5, applied.ReplacedConstants);
        Assert.Contains(expected: "goodbye", JavaClassFile.Read(applied.Bytes ?? []).GetStrings());
        Assert.Equal(expected: "constant int 12345 not found", missing.SkippedReason);
    }

    private static string OwnerOfFirstCall(JavaClassFile file, string method)
    {
        Instruction call = file.GetCode(method)
            .Instructions
            .First(static instruction => instruction.Opcode is Opcodes.InvokeStatic);

        return file.GetMemberReference(call.ConstantIndex ?? 0).Owner;
    }
}

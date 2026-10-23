using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.ClassFiles;

/// <summary>Verifies <see cref="ClassPatch.When"/>: conditions over a method's code that decide whether a patch applies, and print themselves in skip reasons.</summary>
public sealed class BodyConditionTests
{
    private const string Helper = "test/Helper";

    private static readonly JavaMethod Put = JavaMethod.Interface(owner: "java/util/Map", name: "put", descriptor: "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;");

    private static readonly JavaMethod PutIfAbsent = JavaMethod.Interface(owner: "java/util/Map", name: "putIfAbsent", descriptor: "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;");

    // static void build(Map map) { lock(); map.put(null, null); counter = new HashMap(); }   static void other(Map map) { map.putIfAbsent(null, null); }
    private static readonly byte[] Subject = new ClassFileWriter(Helper)
        .AddMethod(
            name: "build",
            descriptor: "(Ljava/util/Map;)V",
            static code => code.Invoke(WriteLock())
                .ALoad(slot: 0)
                .AConstNull()
                .AConstNull()
                .Invoke(Put)
                .Pop()
                .New(className: "java/util/HashMap")
                .Dup()
                .InvokeSpecial(owner: "java/util/HashMap", name: "<init>", descriptor: "()V")
                .PutStatic(Helper, name: "counter", descriptor: "Ljava/util/Map;")
                .Return()
        )
        .AddMethod(
            name: "other",
            descriptor: "(Ljava/util/Map;)V",
            static code => code.ALoad(slot: 0).AConstNull().AConstNull().Invoke(PutIfAbsent).Pop().Return()
        )
        .ToArray();

    /// <summary>A condition that holds lets the edits that follow apply, with no line of its own in the result.</summary>
    [Fact]
    public void AConditionThatHoldsLetsThePatchApply()
    {
        ClassPatchResult result = ClassPatch.For(Helper)
            .Method(name: "build", descriptor: "(Ljava/util/Map;)V")
            .When(static body => body.Calls(WriteLock()) && body.Calls(Put) && !body.Calls(PutIfAbsent))
            .RedirectInvocation(Put, toOwner: "test/Maps")
            .Apply(Subject);

        Assert.True(result.Changed, result.SkippedReason);
        Assert.Equal(["build(Ljava/util/Map;)V: 1 invocation of java/util/Map.put redirected"], result.Details);
    }

    /// <summary>A condition that fails skips the class, and the reason prints the condition the way it was written, with the scope.</summary>
    [Fact]
    public void AFailedConditionPrintsItself()
    {
        ClassPatchResult result = ClassPatch.For(Helper)
            .Method(name: "other", descriptor: "(Ljava/util/Map;)V")
            .When(static body => body.Calls(WriteLock()) && body.Calls(Put) && !body.Calls(PutIfAbsent))
            .RedirectInvocation(PutIfAbsent, toOwner: "test/Maps")
            .Apply(Subject);

        Assert.False(result.Changed);
        Assert.Equal(expected: "other(Ljava/util/Map;)V: requires writeLock() && put() && !putIfAbsent()", result.SkippedReason);
    }

    /// <summary>Fields and constructions can be tested too, and the printed condition keeps the grouping of or under and, and of anything under not.</summary>
    [Fact]
    public void FieldsConstructionsAndGroupingPrint()
    {
        JavaMethod captured = PutIfAbsent;

        ClassPatchResult grouped = ClassPatch.For(Helper)
            .Method(name: "build")
            .When(
                body => body.Creates(className: "java/util/HashMap") && (body.AccessesField(Helper, name: "counter") || body.Calls(captured)) && !(body.Calls(captured) || body.Creates(className: "java/util/TreeMap"))
            )
            .ReplaceConstruction(fromClass: "java/util/HashMap", toClass: "java/util/TreeMap")
            .Apply(Subject);

        ClassPatchResult failed = ClassPatch.For(Helper).When(static body => !(body.Creates(className: "java/util/HashMap") || body.Calls(Put))).ReplaceConstant(oldValue: 1, newValue: 2).Apply(Subject);

        Assert.True(grouped.Changed, grouped.SkippedReason);
        Assert.Equal(expected: "requires !(new HashMap || put())", failed.SkippedReason);
    }

    /// <summary>
    /// Anything that is not a test of the body combined with and, or and not is refused when the patch is built, as is an argument that depends on the body, so a condition never runs code at class load time.
    /// </summary>
    [Fact]
    public void RefusesWhatIsNotACondition()
    {
        ArgumentException other = Assert.Throws<ArgumentException>(static () => ClassPatch.For(Helper).When(static body => body.GetHashCode() == 1));
        ArgumentException dependent = Assert.Throws<ArgumentException>(static () => ClassPatch.For(Helper).When(static body => body.Creates(body.ToString() ?? string.Empty)));
        ArgumentException dotted = Assert.Throws<ArgumentException>(static () => ClassPatch.For(Helper).When(static body => body.Creates(className: "java.util.HashMap")));

        Assert.Contains(expectedSubstring: "When condition", other.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "When condition", dependent.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "slashes", dotted.Message, StringComparison.Ordinal);
    }

    private static JavaMethod WriteLock()
    {
        return JavaMethod.Static(owner: "test/Locks", name: "writeLock", descriptor: "()V");
    }
}

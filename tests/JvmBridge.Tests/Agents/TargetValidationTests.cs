using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.Agents;

/// <summary>Verifies that redirect targets, construction factories and advice hooks in helper classes of the agent are checked against what the helpers declare.</summary>
[Collection(FakeJvm.CollectionName)]
public sealed class TargetValidationTests
{
    private const string Helper = "test/Helpers";

    private static readonly JavaMethod Length = JavaMethod.Virtual(owner: "java/lang/String", name: "length", descriptor: "()I");

    // class Helpers { static int length(String value) { return 0; } int size(Object value) { return 0; } static Set names() { return null; } }
    private static readonly JavaClassDefinition Helpers = JavaClassDefinition.FromBytes(
        new ClassFileWriter(Helper)
            .AddMethod(name: "length", descriptor: "(Ljava/lang/String;)I", static code => code.IConst(value: 0).IReturn())
            .AddMethod(name: "size", descriptor: "(Ljava/lang/Object;)I", static code => code.IConst(value: 0).IReturn(), accessFlags: 0x0001)
            .AddMethod(name: "names", descriptor: "()Ljava/util/Set;", static code => code.AConstNull().AReturn())
            .ToArray()
    );

    /// <summary>A patch whose targets the helper declares as static passes, and a target in a class that is not a helper, such as one of the JDK, is not checked.</summary>
    [Fact]
    public void DeclaredTargetsPass()
    {
        ClassPatch patch = ClassPatch.For(className: "test/Subject")
            .RedirectInvocation(Length, Helper)
            .ReplaceConstruction(fromClass: "java/util/HashSet", JavaMethod.Static(Helper, name: "names", descriptor: "()Ljava/util/Set;"))
            .RedirectInvocation(JavaMethod.Virtual(owner: "java/util/List", name: "size", descriptor: "()I"), toOwner: "java/util/Collections");

        Assert.Empty(patch.FindMissingTargets([Helpers]));
    }

    /// <summary>A redirect to a descriptor the helper lacks, a hook it lacks and a target it declares as an instance method are each reported with the patch, the helper and the method.</summary>
    [Fact]
    public void MissingTargetsAreReported()
    {
        ClassPatch patch = ClassPatch.For(className: "test/Subject")
            .RedirectInvocation(Length, JavaMethod.Static(Helper, name: "length", descriptor: "(Ljava/lang/Object;)I"))
            .RedirectInvocation(
                JavaMethod.Virtual(owner: "java/util/List", name: "size", descriptor: "()I"),
                JavaMethod.Static(Helper, name: "size", descriptor: "(Ljava/lang/Object;)I")
            )
            .Method(name: "run", descriptor: "()V")
            .Before(JavaMethod.Static(Helper, name: "before", descriptor: "(Ljava/lang/Object;)V"));

        IReadOnlyList<string> missing = patch.FindMissingTargets([Helpers]);

        Assert.Equal(
            [
                "the patch of test/Subject calls test/Helpers.length(Ljava/lang/Object;)I, which the helper test/Helpers does not declare",
                "the patch of test/Subject calls test/Helpers.size(Ljava/lang/Object;)I, which the helper test/Helpers does not declare as static",
                "the patch of test/Subject calls test/Helpers.before(Ljava/lang/Object;)V, which the helper test/Helpers does not declare",
            ],
            missing
        );
    }

    /// <summary>Registering a patch whose target a defined helper lacks fails, and so does defining a helper that lacks the target of a registered patch, whichever comes first in Configure.</summary>
    [Fact]
    public void RegistrationFailsInEitherOrder()
    {
        using FakeJvm jvm = new();

        ClassPatch broken = ClassPatch.For(className: "test/Subject").RedirectInvocation(Length, JavaMethod.Static(Helper, name: "measure", descriptor: "(Ljava/lang/String;)I"));

        AgentContext helperFirst = jvm.CreateContext(name: "helper-first");
        helperFirst.DefineClass(Helpers, ClassLoaderTarget.Bootstrap);
        ArgumentException patchRefused = Assert.Throws<ArgumentException>(() => helperFirst.Patch(broken));

        AgentContext patchFirst = jvm.CreateContext(name: "patch-first");
        patchFirst.Patch(broken);
        ArgumentException helperRefused = Assert.Throws<ArgumentException>(() => patchFirst.DefineClass(Helpers, ClassLoaderTarget.Bootstrap));

        Assert.Contains(expectedSubstring: "test/Helpers.measure(Ljava/lang/String;)I", patchRefused.Message, StringComparison.Ordinal);
        Assert.Equal(patchRefused.Message, helperRefused.Message);
    }
}

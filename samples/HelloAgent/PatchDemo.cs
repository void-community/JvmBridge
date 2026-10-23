using JvmBridge.Agents;
using JvmBridge.ClassFiles;

namespace HelloAgent;

/// <summary>Declarative patches: when BridgeFixture.PatchTarget loads, three of its operations change, using helper classes the agent defines.</summary>
internal static class PatchDemo
{
    internal static readonly ClassPatch Target = ClassPatch.For(className: "BridgeFixture$PatchTarget")
        .Method(name: "length", descriptor: "(Ljava/lang/String;)I")
        .RedirectInvocation(JavaMethod.Virtual(owner: "java/lang/String", name: "length", descriptor: "()I"), BootstrapHelper)
        .Method(name: "sorted")
        .RedirectInvocation(
            JavaMethod.Static(owner: "java/util/Collections", name: "sort", descriptor: "(Ljava/util/List;Ljava/util/Comparator;)V"),
            LoaderHelper
        )
        .AnyMethod()
        .ReplaceConstant(oldValue: "patch-original", newValue: "patch-replaced");
    // Patches that match classes instead of naming one: by supertype, by class-level annotation and by a filter on the class name.
    internal static readonly ClassPatch Animals = ClassPatch.ForSubclassesOf(superType: "BridgeFixture$Animal").ReplaceConstant(oldValue: "woof", newValue: "bark");

    internal static readonly ClassPatch Tagged = ClassPatch.ForAnnotatedWith(annotationType: "BridgeFixture$Marker").ReplaceConstant(oldValue: "tagged-original", newValue: "tagged-patched");

    internal static readonly ClassPatch Selected = ClassPatch.ForMatching(static name => name.EndsWith(value: "$Selected", StringComparison.Ordinal), description: "classes named Selected")
        .ReplaceConstant(oldValue: "selected-original", newValue: "selected-patched");

    // A class of java.base (a named module) patched to call the helper in the bootstrap loader: StringJoiner's null checks no longer check.
    internal static readonly ClassPatch JavaBase = ClassPatch.For(className: "java/util/StringJoiner")
        .RedirectInvocation(
            JavaMethod.Static(owner: "java/util/Objects", name: "requireNonNull", descriptor: "(Ljava/lang/Object;Ljava/lang/String;)Ljava/lang/Object;"),
            BootstrapHelper
        );

    // Advice around three methods: a static one and an instance one get an After hook that changes the result, and a static one gets a Before hook that records its argument. Advised loads after the agent; AdvisedEarly loads before a late attach, which is too late for advice.
    internal static readonly ClassPatch Advice = ClassPatch.For(className: "BridgeFixture$Advised")
        .Method(name: "add", descriptor: "(II)I")
        .After(JavaMethod.Static(AdviceHooks, name: "afterAdd", descriptor: "(Ljava/lang/Object;I)I"))
        .Method(name: "twice", descriptor: "(I)I")
        .After(JavaMethod.Static(AdviceHooks, name: "afterTwice", descriptor: "(Ljava/lang/Object;I)I"))
        .Method(name: "greet", descriptor: "(Ljava/lang/String;)Ljava/lang/String;")
        .Before(JavaMethod.Static(AdviceHooks, name: "beforeGreet", descriptor: "(Ljava/lang/Object;Ljava/lang/String;)V"));

    internal static readonly ClassPatch AdviceEarly = ClassPatch.For(className: "BridgeFixture$AdvisedEarly")
        .Method(name: "add", descriptor: "(II)I")
        .After(JavaMethod.Static(AdviceHooks, name: "afterAdd", descriptor: "(Ljava/lang/Object;I)I"));

    private const string AdviceHooks = "jvmbridge/sample/AdviceHooks";

    private const string BootstrapHelper = "jvmbridge/sample/BootstrapPatchHelpers";

    private const string LoaderHelper = "jvmbridge/sample/LoaderPatchHelpers";

    /// <summary>Registers the demo patches that a command controls again and retransforms the loaded classes they target.</summary>
    internal static IReadOnlyList<PatchReport> PatchNow(AgentContext context) => [context.PatchNow(Target), context.PatchNow(Advice), context.PatchNow(AdviceEarly)];

    internal static void Register(AgentContext context)
    {
        context.DefineClass(CreateBootstrapHelper(), ClassLoaderTarget.Bootstrap);
        context.DefineClass(CreateLoaderHelper(), ClassLoaderTarget.PatchedClassLoader);
        context.Patch(Target);
        context.Patch(Animals);
        context.Patch(Tagged);
        context.Patch(Selected);
        context.Patch(JavaBase);
        context.DefineClass(CreateAdviceHooks(), ClassLoaderTarget.Bootstrap);
        context.Patch(Advice);
        context.Patch(AdviceEarly);
    }

    /// <summary>Removes every demo patch that a command controls and retransforms the loaded classes they target.</summary>
    internal static IReadOnlyList<PatchReport> Unpatch(AgentContext context) => [context.Unpatch(Target), context.Unpatch(Advice), context.Unpatch(AdviceEarly)];

    // The hooks of the advice: afterAdd(Object self, int result) { return result + 1000; }, afterTwice(Object self, int result) { return result + 1; } and beforeGreet(Object self, String name) { System.setProperty("advice.before", name); }.
    private static JavaClassDefinition CreateAdviceHooks()
    {
        JavaMethod setProperty = JavaMethod.Static(owner: "java/lang/System", name: "setProperty", descriptor: "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;");

        return JavaClassDefinition.FromBytes(
            new ClassFileWriter(AdviceHooks).AddMethod(
                name: "afterAdd",
                descriptor: "(Ljava/lang/Object;I)I",
                static code => code.ILoad(slot: 1).IConst(value: 1000).Op(Opcodes.IAdd).IReturn()
            )
                .AddMethod(
                    name: "afterTwice",
                    descriptor: "(Ljava/lang/Object;I)I",
                    static code => code.ILoad(slot: 1).IConst(value: 1).Op(Opcodes.IAdd).IReturn()
                )
                .AddMethod(
                    name: "beforeGreet",
                    descriptor: "(Ljava/lang/Object;Ljava/lang/String;)V",
                    code => code.Ldc(value: "advice.before").ALoad(slot: 1).Invoke(setProperty).Pop().Return()
                )
                .ToArray()
        );
    }

    // static int length(String value) { return 1000 + value.length(); } and static Object requireNonNull(Object value, String message) { return value; }
    private static JavaClassDefinition CreateBootstrapHelper()
    {
        JavaMethod length = JavaMethod.Virtual(owner: "java/lang/String", name: "length", descriptor: "()I");

        return JavaClassDefinition.FromBytes(
            new ClassFileWriter(BootstrapHelper).AddMethod(
                name: "length",
                descriptor: "(Ljava/lang/String;)I",
                code => code.IConst(value: 1000).ALoad(slot: 0).Invoke(length).Op(Opcodes.IAdd).IReturn()
            )
                .AddMethod(
                    name: "requireNonNull",
                    descriptor: "(Ljava/lang/Object;Ljava/lang/String;)Ljava/lang/Object;",
                    static code => code.ALoad(slot: 0).AReturn()
                )
                .ToArray()
        );
    }

    // static void sort(List list, Comparator comparator) { Collections.reverse(list); }
    private static JavaClassDefinition CreateLoaderHelper()
    {
        JavaMethod reverse = JavaMethod.Static(owner: "java/util/Collections", name: "reverse", descriptor: "(Ljava/util/List;)V");

        return JavaClassDefinition.FromBytes(
            new ClassFileWriter(LoaderHelper).AddMethod(name: "sort", descriptor: "(Ljava/util/List;Ljava/util/Comparator;)V", code => code.ALoad(slot: 0).Invoke(reverse).Return()).ToArray()
        );
    }
}

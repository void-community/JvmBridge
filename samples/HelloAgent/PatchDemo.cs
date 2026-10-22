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
        ClassFileWriter writer = new(AdviceHooks);
        int setProperty = writer.AddMethodReference(owner: "java/lang/System", name: "setProperty", descriptor: "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;");
        int key = writer.AddString(value: "advice.before");

        // iload_1; sipush 1000; iadd; ireturn
        byte[] afterAdd = [0x1b, 0x11, 0x03, 0xe8, 0x60, 0xac];

        // iload_1; iconst_1; iadd; ireturn
        byte[] afterTwice = [0x1b, 0x04, 0x60, 0xac];

        // ldc key; aload_1; invokestatic System.setProperty; pop; return
        byte[] beforeGreet = [0x12, (byte)key, 0x2b, 0xb8, (byte)(setProperty >> 8), (byte)setProperty, 0x57, 0xb1];

        return JavaClassDefinition.FromBytes(
            writer.AddMethod(name: "afterAdd", descriptor: "(Ljava/lang/Object;I)I", maxStack: 2, maxLocals: 2, afterAdd)
                .AddMethod(name: "afterTwice", descriptor: "(Ljava/lang/Object;I)I", maxStack: 2, maxLocals: 2, afterTwice)
                .AddMethod(name: "beforeGreet", descriptor: "(Ljava/lang/Object;Ljava/lang/String;)V", maxStack: 2, maxLocals: 2, beforeGreet)
                .ToArray()
        );
    }

    // static int length(String value) { return 1000 + value.length(); }
    private static JavaClassDefinition CreateBootstrapHelper()
    {
        ClassFileWriter writer = new(BootstrapHelper);
        int length = writer.AddMethodReference(owner: "java/lang/String", name: "length", descriptor: "()I");

        // sipush 1000; aload_0; invokevirtual String.length; iadd; ireturn
        byte[] code = [0x11, 0x03, 0xe8, 0x2a, 0xb6, (byte)(length >> 8), (byte)length, 0x60, 0xac];

        // static Object requireNonNull(Object value, String message) { return value; }
        byte[] passThrough = [0x2a, 0xb0];

        return JavaClassDefinition.FromBytes(
            writer.AddMethod(name: "length", descriptor: "(Ljava/lang/String;)I", maxStack: 2, maxLocals: 1, code)
                .AddMethod(
                    name: "requireNonNull",
                    descriptor: "(Ljava/lang/Object;Ljava/lang/String;)Ljava/lang/Object;",
                    maxStack: 1,
                    maxLocals: 2,
                    passThrough
                )
                .ToArray()
        );
    }

    // static void sort(List list, Comparator comparator) { Collections.reverse(list); }
    private static JavaClassDefinition CreateLoaderHelper()
    {
        ClassFileWriter writer = new(LoaderHelper);
        int reverse = writer.AddMethodReference(owner: "java/util/Collections", name: "reverse", descriptor: "(Ljava/util/List;)V");

        // aload_0; invokestatic Collections.reverse; return
        byte[] code = [0x2a, 0xb8, (byte)(reverse >> 8), (byte)reverse, 0xb1];

        return JavaClassDefinition.FromBytes(
            writer.AddMethod(name: "sort", descriptor: "(Ljava/util/List;Ljava/util/Comparator;)V", maxStack: 1, maxLocals: 2, code).ToArray()
        );
    }
}

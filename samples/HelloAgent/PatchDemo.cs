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
    private const string BootstrapHelper = "jvmbridge/sample/BootstrapPatchHelpers";

    private const string LoaderHelper = "jvmbridge/sample/LoaderPatchHelpers";

    internal static void Register(AgentContext context)
    {
        context.DefineClass(CreateBootstrapHelper(), ClassLoaderTarget.Bootstrap);
        context.DefineClass(CreateLoaderHelper(), ClassLoaderTarget.PatchedClassLoader);
        context.Patch(Target);
    }

    // static int length(String value) { return 1000 + value.length(); }
    private static JavaClassDefinition CreateBootstrapHelper()
    {
        ClassFileWriter writer = new(BootstrapHelper);
        int length = writer.AddMethodReference(owner: "java/lang/String", name: "length", descriptor: "()I");

        // sipush 1000; aload_0; invokevirtual String.length; iadd; ireturn
        byte[] code = [0x11, 0x03, 0xe8, 0x2a, 0xb6, (byte)(length >> 8), (byte)length, 0x60, 0xac];

        return JavaClassDefinition.FromBytes(writer.AddMethod(name: "length", descriptor: "(Ljava/lang/String;)I", maxStack: 2, maxLocals: 1, code).ToArray());
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

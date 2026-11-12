using JvmBridge.Agents;
using JvmBridge.ClassFiles;

namespace HelloAgent;

/// <summary>
/// Helpers that call <c>jdk.internal.misc.VM.isBooted()</c>, a public method in a package that java.base exports to no unnamed module: one in the bootstrap loader, to whose module the package is exported, and one in each loader of a patched class, to
/// whose module it is opened. BridgeFixture$ModuleTarget reaches them through patched calls of <c>Boolean.getBoolean</c>. With the <c>no-exports</c> option the agent exports a module and a package that do not exist instead, so both helpers fail to link.
/// </summary>
internal static class ModuleDemo
{
    private const string BootstrapHelper = "jvmbridge/sample/BootstrapModuleHelpers";

    private const string LoaderHelper = "jvmbridge/sample/LoaderModuleHelpers";

    private static readonly JavaMethod GetBoolean = JavaMethod.Static(owner: "java/lang/Boolean", name: "getBoolean", descriptor: "(Ljava/lang/String;)Z");

    private static readonly ClassPatch Target = ClassPatch.For(className: "BridgeFixture$ModuleTarget")
        .Method(name: "bootstrap")
        .RedirectInvocation(GetBoolean, BootstrapHelper)
        .Method(name: "loader")
        .RedirectInvocation(GetBoolean, LoaderHelper);

    internal static void Register(AgentContext context)
    {
        JavaClassDefinition bootstrap = CreateHelper(BootstrapHelper);
        JavaClassDefinition loader = CreateHelper(LoaderHelper);

        context.DefineClass(bootstrap, ClassLoaderTarget.Bootstrap);
        context.DefineClass(loader, ClassLoaderTarget.PatchedClassLoader);
        context.Patch(Target);

        if (context.Options.Contains(value: "no-exports", StringComparison.Ordinal))
        {
            context.ExportPackage(module: "jvmbridge.missing", package: "jdk/internal/misc", bootstrap);
            context.OpenPackage(module: "java.base", package: "jvmbridge/missing", loader);

            return;
        }

        context.ExportPackage(module: "java.base", package: "jdk/internal/misc", bootstrap);
        context.OpenPackage(module: "java.base", package: "jdk/internal/misc", loader);
    }

    // static boolean getBoolean(String name) { return VM.isBooted(); }
    private static JavaClassDefinition CreateHelper(string name)
    {
        JavaMethod isBooted = JavaMethod.Static(owner: "jdk/internal/misc/VM", name: "isBooted", descriptor: "()Z");

        return JavaClassDefinition.FromBytes(
            new ClassFileWriter(name).AddMethod(name: "getBoolean", descriptor: "(Ljava/lang/String;)Z", code => code.Invoke(isBooted).IReturn()).ToArray()
        );
    }
}

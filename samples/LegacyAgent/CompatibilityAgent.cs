using System.Globalization;

using JvmBridge.Agents;
using JvmBridge.ClassFiles;
using JvmBridge.Runtime;

namespace LegacyAgent;

/// <summary>
/// An agent for Java 6 and later, from one build: it patches a constant, redirects a call to a C# helper, advises a method with a hook compiled from Java, counts events, and attaches a thread of its own, which are the parts that
/// depend on the JNI and JVMTI versions and the class file versions that JvmBridge negotiates and writes. It also redirects a call to a typed Java helper in the application's own loader, replaces two constructions and removes a fluent call.
/// </summary>
internal sealed class CompatibilityAgent : JavaAgent
{
    private const string Account = "legacy/LegacyFixture$Account";

    private const string Fixture = "legacy/LegacyFixture";

    // A typed helper in the application's loader and package reads the account's private balance, which this patch opens, instead of the deposit running.
    private static readonly ClassPatch OpenAccount = ClassPatch.For(Account).SetFieldAccess(name: "balance", accessFlags: 0x0001);

    private static readonly ClassPatch Typed = ClassPatch.For(Fixture)
        .ReplaceConstruction(fromClass: "java/util/ArrayList", toClass: "java/util/concurrent/CopyOnWriteArrayList", storedIn: "SENT")
        .ReplaceConstruction(
            fromClass: "java/util/HashSet",
            JavaMethod.Static(owner: "legacy/TypedHelpers", name: "newNames", descriptor: "()Ljava/util/Set;"),
            storedIn: "NAMES"
        )
        .Method(name: "deposited", descriptor: "()I")
        .RedirectInvocation(
            JavaMethod.Virtual(Account, name: "deposit", descriptor: "(I)I"),
            JavaMethod.Static(owner: "legacy/TypedHelpers", name: "deposit", descriptor: "(Llegacy/LegacyFixture$Account;I)I")
        )
        .Method(name: "main", descriptor: "([Ljava/lang/String;)V")
        .RemoveInvocation(JavaMethod.Virtual(owner: "legacy/LegacyFixture$Settings", name: "autosave", descriptor: "()Llegacy/LegacyFixture$Settings;"));

    private static readonly ClassPatch Patch = ClassPatch.For(Fixture)
        .ReplaceConstant(oldValue: "legacy-original", newValue: "legacy-patched")
        .Method(name: "length", descriptor: "(Ljava/lang/String;)I")
        .RedirectInvocation(JavaMethod.Virtual(owner: "java/lang/String", name: "length", descriptor: "()I"), toOwner: "jvmbridge/legacy/Interop")
        .Method(name: "add", descriptor: "(II)I")
        .After(JavaMethod.Static(owner: "jvmbridge/legacy/Hooks", name: "afterAdd", descriptor: "(Ljava/lang/Object;I)I"));

    /// <inheritdoc/>
    public override void Configure(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.DefineClass(Interop.Definition, ClassLoaderTarget.Bootstrap);
        context.DefineClass(JavaClasses.Jvmbridge.Legacy.Hooks, ClassLoaderTarget.Bootstrap);
        context.Patch(Patch);
        context.DefineClass(JavaClasses.Legacy.TypedHelpers, ClassLoaderTarget.PatchedClassLoader);
        context.Patch(OpenAccount);
        context.Patch(Typed);
        Interop.Subscription = context.Events.Subscribe(JavaEvents.ThreadStart | JavaEvents.ClassLoad).ToString();
    }

    /// <inheritdoc/>
    public override void OnAttach(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        OnLoad(context);
        context.Log("ATTACH java=" + context.JavaMajor.ToString(CultureInfo.InvariantCulture));
        AttachWorker(context.VirtualMachine);
    }

    /// <inheritdoc/>
    public override void OnClassLoad(AgentContext context, JavaEnvironment environment, nint thread, nint type)
    {
        Interop.ClassLoaded();
    }

    /// <inheritdoc/>
    public override void OnLoad(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Log(message: "LOAD");
    }

    /// <inheritdoc/>
    public override void OnThreadStart(AgentContext context, JavaEnvironment environment, nint thread)
    {
        Interop.ThreadStarted();
    }

    /// <inheritdoc/>
    public override void OnUnload(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Log(message: "UNLOAD");
    }

    /// <inheritdoc/>
    public override void OnVmInit(AgentContext context, JavaEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(context);
        // The JVM sets java.specification.version while it initializes, so the Java version is known from here on, not in OnLoad.
        context.Log("VM_INIT java=" + context.JavaMajor.ToString(CultureInfo.InvariantCulture));

        AttachWorker(context.VirtualMachine);
    }

    private static string Attach(JavaVirtualMachine machine)
    {
        try
        {
            using JavaThreadAttachment attachment = machine.AttachCurrentThread();

            using JavaLocalReference system = attachment.Environment.FindClass(name: "java/lang/System");

            return system.Handle == 0 ? "no class" : "ok";
        }
        catch (JniException failure)
        {
            return "failed " + failure.Message;
        }
    }

    // A thread that the JVM did not start attaches with the JNI version that Java 6 knows, and calls into Java.
    private static void AttachWorker(JavaVirtualMachine machine)
    {
        Thread worker = new(() => Interop.Attached = Attach(machine)) { IsBackground = true };
        worker.Start();
        worker.Join();
    }
}

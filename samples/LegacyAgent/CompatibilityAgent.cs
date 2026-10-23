using System.Globalization;

using JvmBridge.Agents;
using JvmBridge.ClassFiles;
using JvmBridge.Runtime;

namespace LegacyAgent;

/// <summary>
/// An agent for Java 6 and later, from one build: it patches a constant, redirects a call to a C# helper, advises a method with a hook compiled from Java, counts events, and attaches a thread of its own, which are the parts that
/// depend on the JNI and JVMTI versions and the class file versions that JvmBridge negotiates and writes.
/// </summary>
internal sealed class CompatibilityAgent : JavaAgent
{
    private const string Fixture = "LegacyFixture";

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
        Interop.Subscription = context.Events.Subscribe(JavaEvents.ThreadStart | JavaEvents.ClassLoad).ToString();
    }

    /// <inheritdoc/>
    public override void OnAttach(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        OnLoad(context);
        context.Log(message: "ATTACH");
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
        context.Log("LOAD java=" + context.JavaMajor.ToString(CultureInfo.InvariantCulture));
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
        context.Log(message: "VM_INIT");

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

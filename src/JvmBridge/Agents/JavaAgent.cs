using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>Lifecycle hooks for an agent. JNI access is deferred until the JVM supplies an environment.</summary>
public abstract class JavaAgent
{
    /// <summary>Requests capabilities before callbacks are installed. No Java calls during startup.</summary>
    /// <param name="context">The agent context in the JVMTI load or live phase.</param>
    public virtual void Configure(AgentContext context) { }

    /// <summary>Handles successful startup after callbacks and requested capabilities are configured.</summary>
    /// <param name="context">The active agent context.</param>
    public virtual void OnLoad(AgentContext context) { }

    /// <summary>Handles attachment to an already running JVM.</summary>
    /// <param name="context">The active agent context.</param>
    public virtual void OnAttach(AgentContext context) => OnLoad(context);

    /// <summary>
    /// Handles another load of this library into a JVM where the agent already runs, which is how a running agent is told what to do: <c>jcmd &lt;pid&gt; JVMTI.agent_load /path/MyAgent.so &lt;command&gt;</c> (Java 9 and later)
    /// or the Attach API's <c>loadAgentPath(path, command)</c>. The runtime does not start a second copy; it calls this on the running agent with the option text of the new load, on the JVM's attach listener thread.
    /// An exception makes the attaching tool report a failed load. The default logs that the command was ignored.
    /// </summary>
    /// <param name="context">The running agent's context.</param>
    /// <param name="command">The option text of the new load.</param>
    public virtual void OnCommand(AgentContext context, string command)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Log("ignored a command because the agent does not handle commands: " + command);
    }

    /// <summary>Handles JVM initialization when ordinary JNI operations first become available.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    public virtual void OnVmInit(AgentContext context, JavaEnvironment environment) { }

    /// <summary>Handles notification that the JVM is beginning shutdown.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    public virtual void OnVmDeath(AgentContext context, JavaEnvironment environment) { }

    /// <summary>Handles preparation of a class in the JVM.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    /// <param name="type">The class reference borrowed for the callback scope.</param>
    public virtual void OnClassPrepare(AgentContext context, JavaEnvironment environment, nint type) { }

    /// <summary>Transforms class bytes before definition or retransformation.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="file">The class name, transformation kind, and borrowed class bytes.</param>
    /// <returns>Owned replacement bytes, or <see langword="null"/> to leave the class unchanged.</returns>
    public virtual byte[]? TransformClass(AgentContext context, ClassFile file) => null;

    /// <summary>Transforms class bytes with borrowed JNI context. References and environment must not escape this callback.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="file">The nullable name, transformation kind, and borrowed bytes.</param>
    /// <param name="callback">Callback references and phase-aware JNI access. Promote references to globals before retaining them.</param>
    /// <returns>Owned replacement bytes, or <see langword="null"/> to leave the class unchanged.</returns>
    public virtual byte[]? TransformClass(AgentContext context, ClassFile file, ClassTransformContext callback) => TransformClass(context, file);

    /// <summary>A thread is about to run. Subscribe with <see cref="JavaEvents.ThreadStart"/>.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    /// <param name="thread">The thread, as a callback-local reference.</param>
    public virtual void OnThreadStart(AgentContext context, JavaEnvironment environment, nint thread) { }

    /// <summary>A thread is about to end. Subscribe with <see cref="JavaEvents.ThreadEnd"/>.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    /// <param name="thread">The thread, as a callback-local reference.</param>
    public virtual void OnThreadEnd(AgentContext context, JavaEnvironment environment, nint thread) { }

    /// <summary>A class was loaded and is not prepared yet. Subscribe with <see cref="JavaEvents.ClassLoad"/>; use <see cref="OnClassPrepare"/> to inspect members.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    /// <param name="thread">The loading thread, as a callback-local reference.</param>
    /// <param name="type">The class, as a callback-local reference.</param>
    public virtual void OnClassLoad(AgentContext context, JavaEnvironment environment, nint thread, nint type) { }

    /// <summary>A Java exception was thrown. Subscribe with <see cref="JavaEvents.Exception"/>. The exception is pending while this runs, so use only JVMTI and the JNI functions that are valid with a pending exception.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    /// <param name="thrown">The thread, method, exception and catch location, all valid for this callback only.</param>
    public virtual void OnException(AgentContext context, JavaEnvironment environment, JavaExceptionEvent thrown) { }

    /// <summary>
    /// A method is about to run. Subscribe with <see cref="JavaEvents.MethodEntry"/>. This runs for every method call, so keep it short and do not call Java from it: that raises the same event for the callee, which the runtime ignores on this thread while a handler runs.
    /// <see cref="AgentContext.DescribeMethod"/> resolves the method.
    /// </summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    /// <param name="thread">The thread, as a callback-local reference.</param>
    /// <param name="method">The raw method ID.</param>
    public virtual void OnMethodEntry(AgentContext context, JavaEnvironment environment, nint thread, nint method) { }

    /// <summary>A method returned or was left by an exception. Subscribe with <see cref="JavaEvents.MethodExit"/>. The same cost and restrictions as <see cref="OnMethodEntry"/> apply.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    /// <param name="thread">The thread, as a callback-local reference.</param>
    /// <param name="method">The raw method ID.</param>
    /// <param name="poppedByException">Whether the method was left by an exception instead of returning.</param>
    public virtual void OnMethodExit(AgentContext context, JavaEnvironment environment, nint thread, nint method, bool poppedByException) { }

    /// <summary>A garbage collection is about to start. Subscribe with <see cref="JavaEvents.GarbageCollectionStart"/>. It runs on a JVM thread while the world is stopped: no JNI, no Java, no allocation of Java objects, and nothing that waits.</summary>
    /// <param name="context">The active agent context.</param>
    public virtual void OnGarbageCollectionStart(AgentContext context) { }

    /// <summary>A garbage collection finished. Subscribe with <see cref="JavaEvents.GarbageCollectionFinish"/>. The restrictions of <see cref="OnGarbageCollectionStart"/> apply.</summary>
    /// <param name="context">The active agent context.</param>
    public virtual void OnGarbageCollectionFinish(AgentContext context) { }

    /// <summary>A thread is about to block on a monitor that another thread holds. Subscribe with <see cref="JavaEvents.MonitorContendedEnter"/>.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    /// <param name="thread">The blocking thread, as a callback-local reference.</param>
    /// <param name="monitor">The monitor object, as a callback-local reference.</param>
    public virtual void OnMonitorContendedEnter(AgentContext context, JavaEnvironment environment, nint thread, nint monitor) { }

    /// <summary>A thread acquired a monitor after blocking on it. Subscribe with <see cref="JavaEvents.MonitorContendedEntered"/>.</summary>
    /// <param name="context">The active agent context.</param>
    /// <param name="environment">The callback-local JNI environment.</param>
    /// <param name="thread">The thread, as a callback-local reference.</param>
    /// <param name="monitor">The monitor object, as a callback-local reference.</param>
    public virtual void OnMonitorContendedEntered(AgentContext context, JavaEnvironment environment, nint thread, nint monitor) { }

    /// <summary>Performs logical cleanup during JVM shutdown without unloading the NativeAOT module.</summary>
    /// <param name="context">The context being released.</param>
    public virtual void OnUnload(AgentContext context) { }
}

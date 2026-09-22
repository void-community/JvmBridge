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

    /// <summary>Performs logical cleanup during JVM shutdown without unloading the NativeAOT module.</summary>
    /// <param name="context">The context being released.</param>
    public virtual void OnUnload(AgentContext context) { }
}

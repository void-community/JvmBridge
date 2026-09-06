using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>Lifecycle hooks for an agent. JNI access is deferred until the JVM supplies an environment.</summary>
public abstract class JavaAgent
{
    /// <summary>Requests capabilities before callbacks are installed. No Java calls during startup.</summary>
    public virtual void Configure(AgentContext context) { }
    public virtual void OnLoad(AgentContext context) { }
    public virtual void OnAttach(AgentContext context) => OnLoad(context);
    public virtual void OnVmInit(AgentContext context, JavaEnvironment environment) { }
    public virtual void OnVmDeath(AgentContext context, JavaEnvironment environment) { }
    public virtual void OnClassPrepare(AgentContext context, JavaEnvironment environment, nint type) { }
    public virtual byte[]? TransformClass(AgentContext context, ClassFile file) => null;
    public virtual void OnUnload(AgentContext context) { }
}

/// <summary>Class bytes borrowed only for the duration of TransformClass. Return owned bytes to replace them.</summary>
public readonly ref struct ClassFile(string? name, bool isRetransformation, ReadOnlySpan<byte> bytes)
{
    public string? Name { get; } = name;
    public bool IsRetransformation { get; } = isRetransformation;
    public ReadOnlySpan<byte> Bytes { get; } = bytes;
}

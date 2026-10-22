namespace JvmBridge.Agents;

/// <summary>A JVMTI capability that JvmBridge can negotiate. Request one with <see cref="AgentContext.TryRequest"/>; subscribing to an event requests what it needs.</summary>
public enum JavaCapability
{
    /// <summary>Retransform classes that are already loaded, which <see cref="AgentContext.PatchNow"/> and <see cref="AgentContext.Unpatch"/> need. It has to be granted before events are enabled.</summary>
    RetransformClasses = 0,

    /// <summary>Replace the bytes of loaded classes with <see cref="AgentContext.RedefineClasses"/>.</summary>
    RedefineClasses = 1,

    /// <summary>Report Java exceptions.</summary>
    GenerateExceptionEvents = 2,

    /// <summary>Report method entry.</summary>
    GenerateMethodEntryEvents = 3,

    /// <summary>Report method exit.</summary>
    GenerateMethodExitEvents = 4,

    /// <summary>Report contention on monitors.</summary>
    GenerateMonitorEvents = 5,

    /// <summary>Report the start and the end of garbage collections.</summary>
    GenerateGarbageCollectionEvents = 6
}

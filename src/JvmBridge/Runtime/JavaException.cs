namespace JvmBridge.Runtime;

/// <summary>A pending Java exception, cleared before returning control to managed code.</summary>
public sealed class JavaException(string operation) : Exception($"Java raised an exception during {operation}.")
{
    /// <summary>Gets the managed operation that observed and cleared the pending Java exception.</summary>
    public string Operation { get; } = operation;
}

namespace JvmBridge.Runtime;

/// <summary>A pending Java exception, cleared before returning control to managed code.</summary>
public sealed class JavaException(string operation) : Exception($"Java raised an exception during {operation}.")
{
    public string Operation { get; } = operation;
}

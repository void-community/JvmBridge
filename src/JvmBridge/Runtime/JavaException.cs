namespace JvmBridge.Runtime;

/// <summary>A pending Java exception, cleared before returning control to managed code.</summary>
public sealed class JavaException(string operation) : Exception($"Java raised an exception during {operation}.")
{
    /// <summary>Gets the managed operation that observed and cleared the pending Java exception.</summary>
    public string Operation { get; } = operation;

    /// <summary>Gets the Java exception's binary class name, if it could be captured.</summary>
    public string? JavaTypeName { get; private set; }

    /// <summary>Gets the Java exception's message, if it could be captured.</summary>
    public string? JavaMessage { get; private set; }

    /// <summary>Gets a bounded Java stack and cause snapshot, if it could be captured.</summary>
    public string? JavaStackTrace { get; private set; }

    internal JavaException(string operation, string? javaTypeName, string? javaMessage, string? javaStackTrace) : this(operation)
    {
        JavaTypeName = javaTypeName;
        JavaMessage = javaMessage;
        JavaStackTrace = javaStackTrace;
    }
}

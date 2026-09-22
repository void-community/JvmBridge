namespace JvmBridge.Runtime;

/// <summary>A JNI invocation failure. The original native result is preserved.</summary>
public sealed class JniException(int errorCode, string operation) : InvalidOperationException($"{operation} failed with JNI error {errorCode}.")
{
    /// <summary>Gets the native JNI result code.</summary>
    public int ErrorCode { get; } = errorCode;

    /// <summary>Gets the managed operation that returned the failure result.</summary>
    public string Operation { get; } = operation;
}

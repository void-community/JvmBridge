namespace JvmBridge.Runtime;

/// <summary>A JNI invocation failure. The original native result is preserved.</summary>
public sealed class JniException(int errorCode, string operation) : InvalidOperationException($"{operation} failed with JNI error {errorCode}.")
{
    public int ErrorCode { get; } = errorCode;
    public string Operation { get; } = operation;
}

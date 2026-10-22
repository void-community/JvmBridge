namespace JvmBridge.Agents;

/// <summary>One frame of a Java stack trace.</summary>
/// <param name="Method">The method that is running in the frame.</param>
/// <param name="Location">The bytecode index in the method, or -1 for a native method.</param>
public readonly record struct JavaStackFrame(JavaMethodName Method, long Location);

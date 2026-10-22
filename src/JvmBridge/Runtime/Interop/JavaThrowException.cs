namespace JvmBridge.Runtime;

/// <summary>
/// Thrown by a C# method that implements a Java <c>native</c> method (<c>[JavaMethod]</c>) to raise a Java exception in the caller. The generated entry point catches it, throws the named Java exception and returns a default value;
/// any other exception is contained and reported without a Java exception, because an unmanaged entry point never lets one escape.
/// </summary>
public sealed class JavaThrowException : Exception
{
    /// <summary>Initializes an exception that raises a Java exception.</summary>
    /// <param name="javaClassName">The internal name of the Java exception class, for example <c>java/lang/IllegalArgumentException</c>; it needs a constructor that takes a string.</param>
    /// <param name="javaMessage">The message of the Java exception, or <see langword="null"/>.</param>
    public JavaThrowException(string javaClassName, string? javaMessage = null)
        : base(javaMessage ?? javaClassName)
    {
        ArgumentException.ThrowIfNullOrEmpty(javaClassName);
        JavaClassName = javaClassName;
        JavaMessage = javaMessage;
    }

    /// <summary>Raises the Java exception in the current thread. Generated entry points call this; a failure to raise it, for example an unknown exception class, is reported and contained, because nothing may escape an unmanaged entry point.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    public void Raise(JavaEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        try
        {
            environment.ThrowNew(JavaClassName, JavaMessage);
        }
        catch (Exception failure) when (failure is JavaException or JniException)
        {
            _ = JvmBridge.Agents.AgentRuntime.ReportFailure(failure);
        }
    }

    /// <summary>Gets the internal name of the Java exception class.</summary>
    public string JavaClassName { get; }

    /// <summary>Gets the message of the Java exception, or <see langword="null"/>.</summary>
    public string? JavaMessage { get; }
}

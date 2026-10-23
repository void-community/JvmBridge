namespace JvmBridge.ClassFiles;

/// <summary>The input is not a well-formed class file, or an edit cannot be applied to it.</summary>
public sealed class ClassFormatException : FormatException
{
    /// <summary>Reports bytes that are not a class file the reader understands, without saying why.</summary>
    public ClassFormatException() { }

    /// <summary>Reports bytes that are not a class file the reader understands, and why.</summary>
    /// <param name="message">The reason the class file was rejected.</param>
    public ClassFormatException(string message) : base(message) { }

    /// <summary>Reports bytes that are not a class file the reader understands, and the failure that showed it.</summary>
    /// <param name="message">The reason the class file was rejected.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ClassFormatException(string message, Exception innerException) : base(message, innerException) { }
}

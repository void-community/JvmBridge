namespace JvmBridge.ClassFiles;

/// <summary>The input is not a well-formed class file, or an edit cannot be applied to it.</summary>
public sealed class ClassFormatException : FormatException
{
    /// <summary>Initializes a new instance of the <see cref="ClassFormatException"/> class.</summary>
    public ClassFormatException() { }

    /// <summary>Initializes a new instance of the <see cref="ClassFormatException"/> class.</summary>
    /// <param name="message">The reason the class file was rejected.</param>
    public ClassFormatException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="ClassFormatException"/> class.</summary>
    /// <param name="message">The reason the class file was rejected.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ClassFormatException(string message, Exception innerException) : base(message, innerException) { }
}

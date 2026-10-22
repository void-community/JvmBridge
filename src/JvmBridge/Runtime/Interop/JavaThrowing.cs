using JvmBridge.Native;

namespace JvmBridge.Runtime;

/// <summary>
/// Raises Java exceptions from native code. After either method returns, an exception is pending in the current thread, and only a few JNI functions may be called until native code returns to Java; return from the native method promptly.
/// </summary>
public static unsafe class JavaThrowing
{
    /// <summary>Raises an existing throwable in the current thread.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="throwable">A reference to a <c>java.lang.Throwable</c>.</param>
    public static void Throw(this JavaEnvironment environment, nint throwable)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.EnsureAccessible();
        int result = environment.Functions->Throw((JNINativeInterface_**)environment.Handle, (_jobject*)throwable);

        if (result != 0)
            throw new JniException(result, operation: nameof(Throw));
    }

    /// <summary>Raises a new Java exception, created with its string constructor, in the current thread.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="className">The internal name of the exception class, for example <c>java/lang/IllegalStateException</c>.</param>
    /// <param name="message">The exception message, or <see langword="null"/>.</param>
    public static void ThrowNew(this JavaEnvironment environment, string className, string? message)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrEmpty(className);
        environment.EnsureAccessible();
        using JavaLocalReference type = environment.FindClass(className);
        byte[]? encoded = message is null ? null : ModifiedUtf8.Encode(message);
        int result;

        fixed (byte* text = encoded)
            result = environment.Functions->ThrowNew((JNINativeInterface_**)environment.Handle, (_jobject*)type.Handle, text);

        if (result != 0)
            throw new JniException(result, operation: nameof(ThrowNew));
    }
}

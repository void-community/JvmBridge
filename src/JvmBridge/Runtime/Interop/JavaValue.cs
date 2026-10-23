using JvmBridge.Native;

namespace JvmBridge.Runtime;

/// <summary>Builds the <see cref="jvalue"/> arguments of a JNI call without unsafe code, one factory per Java type. The generated typed proxies use them; so can any caller of the <c>Call*</c> methods of <see cref="JavaEnvironment"/>.</summary>
public static unsafe class JavaValue
{
    /// <summary>Creates a Java <c>boolean</c> argument.</summary>
    /// <param name="value">The Java <c>boolean</c> to pass in the argument slot.</param>
    /// <returns>The argument.</returns>
    public static jvalue Boolean(bool value)
    {
        jvalue result = default;
        result.z = value ? (byte)1 : (byte)0;

        return result;
    }

    /// <summary>Creates a Java <c>byte</c> argument.</summary>
    /// <param name="value">The Java <c>byte</c> to pass in the argument slot.</param>
    /// <returns>The argument.</returns>
    public static jvalue Byte(sbyte value)
    {
        jvalue result = default;
        result.b = value;

        return result;
    }

    /// <summary>Creates a Java <c>char</c> argument.</summary>
    /// <param name="value">The Java <c>char</c> to pass in the argument slot.</param>
    /// <returns>The argument.</returns>
    public static jvalue Char(char value)
    {
        jvalue result = default;
        result.c = value;

        return result;
    }

    /// <summary>Creates a Java <c>double</c> argument.</summary>
    /// <param name="value">The Java <c>double</c> to pass in the argument slot.</param>
    /// <returns>The argument.</returns>
    public static jvalue Double(double value)
    {
        jvalue result = default;
        result.d = value;

        return result;
    }

    /// <summary>Creates a Java <c>float</c> argument.</summary>
    /// <param name="value">The Java <c>float</c> to pass in the argument slot.</param>
    /// <returns>The argument.</returns>
    public static jvalue Float(float value)
    {
        jvalue result = default;
        result.f = value;

        return result;
    }

    /// <summary>Creates a Java <c>int</c> argument.</summary>
    /// <param name="value">The Java <c>int</c> to pass in the argument slot.</param>
    /// <returns>The argument.</returns>
    public static jvalue Int(int value)
    {
        jvalue result = default;
        result.i = value;

        return result;
    }

    /// <summary>Creates a Java <c>long</c> argument.</summary>
    /// <param name="value">The Java <c>long</c> to pass in the argument slot.</param>
    /// <returns>The argument.</returns>
    public static jvalue Long(long value)
    {
        jvalue result = default;
        result.j = value;

        return result;
    }

    /// <summary>Creates an object argument.</summary>
    /// <param name="value">A reference to the object, or 0 for <see langword="null"/>.</param>
    /// <returns>The argument.</returns>
    public static jvalue Object(nint value)
    {
        jvalue result = default;
        result.l = (_jobject*)value;

        return result;
    }

    /// <summary>Creates a Java <c>short</c> argument.</summary>
    /// <param name="value">The Java <c>short</c> to pass in the argument slot.</param>
    /// <returns>The argument.</returns>
    public static jvalue Short(short value)
    {
        jvalue result = default;
        result.s = value;

        return result;
    }
}

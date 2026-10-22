using JvmBridge.Native;

namespace JvmBridge.Runtime;

/// <summary>
/// Java arrays for a <see cref="JavaEnvironment"/>: primitive arrays that copy to and from spans, object arrays, and string arrays. Element types are <see cref="bool"/>, <see cref="sbyte"/> and <see cref="byte"/> (both Java <c>byte</c>),
/// <see cref="char"/>, <see cref="short"/>, <see cref="int"/>, <see cref="long"/>, <see cref="float"/> and <see cref="double"/>. Every method follows the usual rules for references: the ones that return an array return a local reference that
/// the caller owns, and a Java exception, such as an index out of range, surfaces as a <see cref="JavaException"/>.
/// </summary>
public static unsafe class JavaArrays
{
    private const int Boolean = 0;
    private const int Byte = 1;
    private const int Character = 2;
    private const int Double = 7;
    private const int Float = 6;
    private const int Integer = 4;
    private const int Long = 5;
    private const int Short = 3;

    /// <summary>Gets the number of elements of a Java array.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="array">The array reference.</param>
    /// <returns>The length.</returns>
    public static int GetArrayLength(this JavaEnvironment environment, nint array)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.EnsureAccessible();
        int length = environment.Functions->GetArrayLength(Handle(environment), (_jobject*)array);
        environment.ThrowIfException(nameof(GetArrayLength));

        return length;
    }

    /// <summary>Gets an element of an object array as a local reference; a null element has an empty reference.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="array">The object array reference.</param>
    /// <param name="index">The element index.</param>
    /// <returns>The element, which the caller owns.</returns>
    public static JavaLocalReference GetObjectArrayElement(this JavaEnvironment environment, nint array, int index)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.EnsureAccessible();
        _jobject* element = environment.Functions->GetObjectArrayElement(Handle(environment), (_jobject*)array, index);
        environment.ThrowIfException(nameof(GetObjectArrayElement));

        return new JavaLocalReference(environment, (nint)element);
    }

    /// <summary>Gets the elements of a <c>String[]</c>.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="array">The array reference.</param>
    /// <returns>The strings, with <see langword="null"/> for a null element.</returns>
    public static string?[] GetStringArray(this JavaEnvironment environment, nint array)
    {
        int length = environment.GetArrayLength(array);
        string?[] values = new string?[length];

        for (int index = 0; index < length; index++)
        {
            using JavaLocalReference element = environment.GetObjectArrayElement(array, index);

            values[index] = element.Handle == 0 ? null : environment.GetString(element.Handle);
        }

        return values;
    }

    /// <summary>Copies elements from a primitive array into a span.</summary>
    /// <typeparam name="TElement">The element type; it must match the array's Java element type.</typeparam>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="array">The primitive array reference.</param>
    /// <param name="start">The index of the first element to copy.</param>
    /// <param name="destination">Receives as many elements as it has room for.</param>
    public static void CopyTo<TElement>(this JavaEnvironment environment, nint array, int start, Span<TElement> destination)
        where TElement : unmanaged
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.EnsureAccessible();
        JNINativeInterface_* functions = environment.Functions;
        JNINativeInterface_** handle = Handle(environment);
        int length = destination.Length;

        fixed (TElement* data = destination)
        {
            switch (Classify<TElement>())
            {
                case Boolean:
                    functions->GetBooleanArrayRegion(handle, (_jobject*)array, start, length, (byte*)data);
                    break;
                case Byte:
                    functions->GetByteArrayRegion(handle, (_jobject*)array, start, length, (sbyte*)data);
                    break;
                case Character:
                    functions->GetCharArrayRegion(handle, (_jobject*)array, start, length, (ushort*)data);
                    break;
                case Short:
                    functions->GetShortArrayRegion(handle, (_jobject*)array, start, length, (short*)data);
                    break;
                case Integer:
                    functions->GetIntArrayRegion(handle, (_jobject*)array, start, length, (int*)data);
                    break;
                case Long:
                    functions->GetLongArrayRegion(handle, (_jobject*)array, start, length, (long*)data);
                    break;
                case Float:
                    functions->GetFloatArrayRegion(handle, (_jobject*)array, start, length, (float*)data);
                    break;
                default:
                    functions->GetDoubleArrayRegion(handle, (_jobject*)array, start, length, (double*)data);
                    break;
            }
        }

        environment.ThrowIfException(nameof(CopyTo));
    }

    /// <summary>Copies a span into a primitive array.</summary>
    /// <typeparam name="TElement">The element type; it must match the array's Java element type.</typeparam>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="array">The primitive array reference.</param>
    /// <param name="start">The index of the first element to overwrite.</param>
    /// <param name="source">The elements to copy.</param>
    public static void CopyFrom<TElement>(this JavaEnvironment environment, nint array, int start, ReadOnlySpan<TElement> source)
        where TElement : unmanaged
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.EnsureAccessible();
        JNINativeInterface_* functions = environment.Functions;
        JNINativeInterface_** handle = Handle(environment);
        int length = source.Length;

        fixed (TElement* data = source)
        {
            switch (Classify<TElement>())
            {
                case Boolean:
                    functions->SetBooleanArrayRegion(handle, (_jobject*)array, start, length, (byte*)data);
                    break;
                case Byte:
                    functions->SetByteArrayRegion(handle, (_jobject*)array, start, length, (sbyte*)data);
                    break;
                case Character:
                    functions->SetCharArrayRegion(handle, (_jobject*)array, start, length, (ushort*)data);
                    break;
                case Short:
                    functions->SetShortArrayRegion(handle, (_jobject*)array, start, length, (short*)data);
                    break;
                case Integer:
                    functions->SetIntArrayRegion(handle, (_jobject*)array, start, length, (int*)data);
                    break;
                case Long:
                    functions->SetLongArrayRegion(handle, (_jobject*)array, start, length, (long*)data);
                    break;
                case Float:
                    functions->SetFloatArrayRegion(handle, (_jobject*)array, start, length, (float*)data);
                    break;
                default:
                    functions->SetDoubleArrayRegion(handle, (_jobject*)array, start, length, (double*)data);
                    break;
            }
        }

        environment.ThrowIfException(nameof(CopyFrom));
    }

    /// <summary>Creates a primitive Java array with the given elements.</summary>
    /// <typeparam name="TElement">The element type, which decides the Java array type: <c>byte[]</c> for <see cref="byte"/> and <see cref="sbyte"/>, <c>int[]</c> for <see cref="int"/>, and so on.</typeparam>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="values">The elements.</param>
    /// <returns>The new array as a local reference that the caller owns.</returns>
    public static JavaLocalReference NewArray<TElement>(this JavaEnvironment environment, ReadOnlySpan<TElement> values)
        where TElement : unmanaged
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.EnsureAccessible();
        JNINativeInterface_* functions = environment.Functions;
        JNINativeInterface_** handle = Handle(environment);
        int kind = Classify<TElement>();
        int length = values.Length;
        _jobject* array;

        if (kind == Boolean)
            array = functions->NewBooleanArray(handle, length);
        else if (kind == Byte)
            array = functions->NewByteArray(handle, length);
        else if (kind == Character)
            array = functions->NewCharArray(handle, length);
        else if (kind == Short)
            array = functions->NewShortArray(handle, length);
        else if (kind == Integer)
            array = functions->NewIntArray(handle, length);
        else if (kind == Long)
            array = functions->NewLongArray(handle, length);
        else if (kind == Float)
            array = functions->NewFloatArray(handle, length);
        else
            array = functions->NewDoubleArray(handle, length);

        environment.ThrowIfException(nameof(NewArray));
        JavaLocalReference result = new(environment, (nint)array);

        if (length > 0)
            environment.CopyFrom((nint)array, start: 0, values);

        return result;
    }

    /// <summary>Creates an object array whose elements are all null.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="elementClass">The class reference of the element type.</param>
    /// <param name="length">The number of elements.</param>
    /// <returns>The new array as a local reference that the caller owns.</returns>
    public static JavaLocalReference NewObjectArray(this JavaEnvironment environment, nint elementClass, int length)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.EnsureAccessible();
        _jobject* array = environment.Functions->NewObjectArray(Handle(environment), length, (_jobject*)elementClass, null);
        environment.ThrowIfException(nameof(NewObjectArray));

        return new JavaLocalReference(environment, (nint)array);
    }

    /// <summary>Creates a <c>String[]</c>.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="values">The strings; a null entry becomes a null element.</param>
    /// <returns>The new array as a local reference that the caller owns.</returns>
    public static JavaLocalReference NewStringArray(this JavaEnvironment environment, IReadOnlyList<string?> values)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(values);
        using JavaLocalReference stringClass = environment.FindClass(name: "java/lang/String");
        JavaLocalReference array = environment.NewObjectArray(stringClass.Handle, values.Count);

        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] is not { } value)
                continue;

            using JavaLocalReference text = environment.NewString(value);
            environment.SetObjectArrayElement(array.Handle, index, text.Handle);
        }

        return array;
    }

    /// <summary>Sets an element of an object array.</summary>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="array">The object array reference.</param>
    /// <param name="index">The element index.</param>
    /// <param name="value">The reference to store, or 0 for null.</param>
    public static void SetObjectArrayElement(this JavaEnvironment environment, nint array, int index, nint value)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.EnsureAccessible();
        environment.Functions->SetObjectArrayElement(Handle(environment), (_jobject*)array, index, (_jobject*)value);
        environment.ThrowIfException(nameof(SetObjectArrayElement));
    }

    /// <summary>Copies a whole primitive array into a new managed array.</summary>
    /// <typeparam name="TElement">The element type; it must match the array's Java element type.</typeparam>
    /// <param name="environment">The JNI environment of the current thread.</param>
    /// <param name="array">The primitive array reference.</param>
    /// <returns>The elements.</returns>
    public static TElement[] ToArray<TElement>(this JavaEnvironment environment, nint array)
        where TElement : unmanaged
    {
        int length = environment.GetArrayLength(array);
        TElement[] values = new TElement[length];

        if (length > 0)
            environment.CopyTo(array, start: 0, values.AsSpan());

        return values;
    }

    private static int Classify<TElement>()
    {
        if (typeof(TElement) == typeof(byte) || typeof(TElement) == typeof(sbyte))
            return Byte;

        if (typeof(TElement) == typeof(bool))
            return Boolean;

        if (typeof(TElement) == typeof(char))
            return Character;

        if (typeof(TElement) == typeof(short))
            return Short;

        if (typeof(TElement) == typeof(int))
            return Integer;

        if (typeof(TElement) == typeof(long))
            return Long;

        if (typeof(TElement) == typeof(float))
            return Float;

        if (typeof(TElement) == typeof(double))
            return Double;

        throw new NotSupportedException($"{typeof(TElement).Name} is not a Java primitive array element type.");
    }

    private static JNINativeInterface_** Handle(JavaEnvironment environment)
    {
        return (JNINativeInterface_**)environment.Handle;
    }
}

using System.Text;

using JvmBridge.Native;

namespace JvmBridge.Runtime;

internal sealed unsafe class JavaExceptionDiagnostics(JNINativeInterface_** environment, JNINativeInterface_* functions)
{
    private const int MaxCauses = 8;
    private const int MaxFrames = 64;
    private const int MaxStackCharacters = 65536;
    private readonly JNINativeInterface_** _environment = environment;
    private readonly JNINativeInterface_* _functions = functions;

    internal static (string? TypeName, string? Message, string? StackTrace) Capture(JNINativeInterface_** environment, JNINativeInterface_* functions, _jobject* throwable)
    {
        return new JavaExceptionDiagnostics(environment, functions).Capture(throwable);
    }

    internal static bool ContainCaptureFailure(Exception exception)
    {
        GC.KeepAlive(exception);
        return true;
    }

    private (string? TypeName, string? Message, string? StackTrace) Capture(_jobject* throwable)
    {
        string? typeName = GetTypeName(throwable);
        string? message = CallString(throwable, "getMessage\0"u8, "()Ljava/lang/String;\0"u8);
        string? printed = PrintStackTrace(throwable);
        string stackTrace = printed is null ? FormatHeader(typeName, message) : LimitStackTrace(printed);
        return (typeName, message, stackTrace);
    }

    private string? PrintStackTrace(_jobject* throwable)
    {
        _jobject* stringWriterType = FindClass("java/io/StringWriter\0"u8);
        if (stringWriterType == null)
            return null;

        try
        {
            _jobject* stringWriter = NewObject(stringWriterType, "()V\0"u8, default);
            if (stringWriter == null)
                return null;

            try
            {
                _jobject* printWriterType = FindClass("java/io/PrintWriter\0"u8);
                if (printWriterType == null)
                    return null;

                try
                {
                    jvalue writer = new() { l = stringWriter };
                    jvalue flush = new() { z = 1 };
                    Span<jvalue> arguments = [writer, flush];
                    _jobject* printWriter = NewObject(printWriterType, "(Ljava/io/Writer;Z)V\0"u8, arguments);
                    if (printWriter == null)
                        return null;

                    try
                    {
                        if (!CallVoid(throwable, "printStackTrace\0"u8, "(Ljava/io/PrintWriter;)V\0"u8, printWriter))
                            return null;
                        return CallString(stringWriter, "toString\0"u8, "()Ljava/lang/String;\0"u8, MaxStackCharacters);
                    }
                    finally
                    {
                        _functions->DeleteLocalRef(_environment, printWriter);
                    }
                }
                finally
                {
                    _functions->DeleteLocalRef(_environment, printWriterType);
                }
            }
            finally
            {
                _functions->DeleteLocalRef(_environment, stringWriter);
            }
        }
        finally
        {
            _functions->DeleteLocalRef(_environment, stringWriterType);
        }
    }

    private static string FormatHeader(string? typeName, string? message)
    {
        return message is null ? typeName ?? "<Java throwable>" : $"{typeName ?? "<Java throwable>"}: {message}";
    }

    private static string LimitStackTrace(string printed)
    {
        StringBuilder output = new();
        int causes = 0;
        int frames = 0;
        foreach (ReadOnlySpan<char> line in printed.AsSpan().EnumerateLines())
        {
            if (line.StartsWith("Caused by: ", StringComparison.Ordinal))
            {
                if (++causes == MaxCauses)
                    break;
                frames = 0;
            }
            else if (line.TrimStart().StartsWith("at ", StringComparison.Ordinal) && ++frames > MaxFrames)
                continue;

            _ = output.Append(line).AppendLine();
        }

        return output.ToString();
    }

    private _jobject* FindClass(ReadOnlySpan<byte> name)
    {
        fixed (byte* encoded = name)
        {
            _jobject* result = _functions->FindClass(_environment, encoded);
            if (!ClearSecondary())
                return result;
            if (result != null)
                _functions->DeleteLocalRef(_environment, result);
            return null;
        }
    }

    private _jobject* NewObject(_jobject* type, ReadOnlySpan<byte> signature, ReadOnlySpan<jvalue> arguments)
    {
        _jmethodID* constructor = GetMethod(type, "<init>\0"u8, signature);
        if (constructor == null)
            return null;

        jvalue emptyArgument = default;
        fixed (jvalue* pinnedValues = arguments)
        {
            jvalue* values = arguments.IsEmpty ? &emptyArgument : pinnedValues;
            _jobject* result = _functions->NewObjectA(_environment, type, constructor, values);
            if (!ClearSecondary())
                return result;
            if (result != null)
                _functions->DeleteLocalRef(_environment, result);
            return null;
        }
    }

    private bool CallVoid(_jobject* receiver, ReadOnlySpan<byte> name, ReadOnlySpan<byte> signature, _jobject* argument)
    {
        _jobject* type = _functions->GetObjectClass(_environment, receiver);
        if (ClearSecondary())
        {
            if (type != null)
                _functions->DeleteLocalRef(_environment, type);
            return false;
        }
        if (type == null)
            return false;

        try
        {
            _jmethodID* method = GetMethod(type, name, signature);
            if (method == null)
                return false;

            jvalue value = new() { l = argument };
            _functions->CallVoidMethodA(_environment, receiver, method, &value);
            return !ClearSecondary();
        }
        finally
        {
            _functions->DeleteLocalRef(_environment, type);
        }
    }

    private string? GetTypeName(_jobject* throwable)
    {
        _jobject* type = _functions->GetObjectClass(_environment, throwable);
        if (ClearSecondary())
        {
            if (type != null)
                _functions->DeleteLocalRef(_environment, type);
            return null;
        }
        if (type == null)
            return null;

        try
        {
            return CallString(type, "getName\0"u8, "()Ljava/lang/String;\0"u8);
        }
        finally
        {
            _functions->DeleteLocalRef(_environment, type);
        }
    }

    private string? CallString(_jobject* receiver, ReadOnlySpan<byte> name, ReadOnlySpan<byte> signature, int maxCharacters = int.MaxValue)
    {
        _jobject* value = CallObject(receiver, name, signature);
        if (value == null)
            return null;

        try
        {
            ushort* characters = _functions->GetStringChars(_environment, value, null);
            if (ClearSecondary())
            {
                if (characters != null)
                    _functions->ReleaseStringChars(_environment, value, characters);
                return null;
            }
            if (characters == null)
                return null;

            try
            {
                int length = _functions->GetStringLength(_environment, value);
                return ClearSecondary() ? null : new string((char*)characters, 0, Math.Min(length, maxCharacters));
            }
            finally
            {
                _functions->ReleaseStringChars(_environment, value, characters);
            }
        }
        finally
        {
            _functions->DeleteLocalRef(_environment, value);
        }
    }

    private _jobject* CallObject(_jobject* receiver, ReadOnlySpan<byte> name, ReadOnlySpan<byte> signature)
    {
        _jobject* type = _functions->GetObjectClass(_environment, receiver);
        if (ClearSecondary())
        {
            if (type != null)
                _functions->DeleteLocalRef(_environment, type);
            return null;
        }
        if (type == null)
            return null;

        try
        {
            _jmethodID* method = GetMethod(type, name, signature);
            if (method == null)
                return null;

            jvalue emptyArgument = default;
            _jobject* result = _functions->CallObjectMethodA(_environment, receiver, method, &emptyArgument);
            if (!ClearSecondary())
                return result;
            if (result != null)
                _functions->DeleteLocalRef(_environment, result);
            return null;
        }
        finally
        {
            _functions->DeleteLocalRef(_environment, type);
        }
    }

    private _jmethodID* GetMethod(_jobject* type, ReadOnlySpan<byte> name, ReadOnlySpan<byte> signature)
    {
        fixed (byte* encodedName = name)
        fixed (byte* encodedSignature = signature)
        {
            _jmethodID* method = _functions->GetMethodID(_environment, type, encodedName, encodedSignature);
            return ClearSecondary() ? null : method;
        }
    }

    private bool ClearSecondary()
    {
        if (_functions->ExceptionCheck(_environment) == 0)
            return false;
        _functions->ExceptionClear(_environment);
        return true;
    }
}

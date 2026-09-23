using System.Text;

using JvmBridge.Native;

namespace JvmBridge.Runtime;

internal sealed unsafe class JavaExceptionDiagnostics(JNINativeInterface_** environment, JNINativeInterface_* functions)
{
    private const int MaxCauses = 8;
    private const int MaxFrames = 64;
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
        Span<nint> seen = stackalloc nint[MaxCauses];
        seen[0] = (nint)throwable;
        int count = 1;
        StringBuilder stackTrace = new();
        string? typeName = null;
        string? message = null;

        try
        {
            for (int index = 0; index < MaxCauses && index < count; index++)
            {
                _jobject* current = (_jobject*)seen[index];
                string? currentType = GetTypeName(current);
                string? currentMessage = CallString(current, "getMessage\0"u8, "()Ljava/lang/String;\0"u8);

                if (index == 0)
                {
                    typeName = currentType;
                    message = currentMessage;
                }

                if (index != 0)
                    _ = stackTrace.Append("Caused by: ");
                _ = stackTrace.Append(currentType ?? "<Java throwable>");
                if (currentMessage is not null)
                    _ = stackTrace.Append(": ").Append(currentMessage);
                _ = stackTrace.AppendLine();

                AppendFrames(stackTrace, current);
                if (index == MaxCauses - 1)
                    break;

                _jobject* cause = CallObject(current, "getCause\0"u8, "()Ljava/lang/Throwable;\0"u8);
                if (cause == null)
                    break;

                bool cycle = false;
                for (int previous = 0; previous < count; previous++)
                {
                    if (_functions->IsSameObject(_environment, cause, (_jobject*)seen[previous]) != 0)
                    {
                        cycle = true;
                        break;
                    }
                }

                if (ClearSecondary() || cycle)
                {
                    _functions->DeleteLocalRef(_environment, cause);
                    break;
                }

                seen[count++] = (nint)cause;
            }
        }
        finally
        {
            _ = ClearSecondary();
            for (int index = 1; index < count; index++)
                _functions->DeleteLocalRef(_environment, (_jobject*)seen[index]);
        }

        return (typeName, message, stackTrace.Length == 0 ? null : stackTrace.ToString());
    }

    private void AppendFrames(StringBuilder output, _jobject* throwable)
    {
        _jobject* frames = CallObject(throwable, "getStackTrace\0"u8, "()[Ljava/lang/StackTraceElement;\0"u8);
        if (frames == null)
            return;

        try
        {
            int length = _functions->GetArrayLength(_environment, frames);
            if (ClearSecondary())
                return;

            for (int index = 0; index < Math.Min(length, MaxFrames); index++)
            {
                _jobject* frame = _functions->GetObjectArrayElement(_environment, frames, index);
                if (ClearSecondary())
                {
                    if (frame != null)
                        _functions->DeleteLocalRef(_environment, frame);
                    continue;
                }
                if (frame == null)
                    continue;

                try
                {
                    string? location = CallString(frame, "toString\0"u8, "()Ljava/lang/String;\0"u8);
                    if (location is not null)
                        _ = output.Append("\tat ").AppendLine(location);
                }
                finally
                {
                    _functions->DeleteLocalRef(_environment, frame);
                }
            }
        }
        finally
        {
            _functions->DeleteLocalRef(_environment, frames);
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

    private string? CallString(_jobject* receiver, ReadOnlySpan<byte> name, ReadOnlySpan<byte> signature)
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
                return ClearSecondary() ? null : new string((char*)characters, 0, length);
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
            fixed (byte* encodedName = name)
            fixed (byte* encodedSignature = signature)
            {
                _jmethodID* method = _functions->GetMethodID(_environment, type, encodedName, encodedSignature);
                if (ClearSecondary() || method == null)
                    return null;

                jvalue emptyArgument = default;
                _jobject* result = _functions->CallObjectMethodA(_environment, receiver, method, &emptyArgument);
                if (!ClearSecondary())
                    return result;
                if (result != null)
                    _functions->DeleteLocalRef(_environment, result);
                return null;
            }
        }
        finally
        {
            _functions->DeleteLocalRef(_environment, type);
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

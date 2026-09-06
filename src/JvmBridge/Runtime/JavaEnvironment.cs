using JvmBridge.Native;

namespace JvmBridge.Runtime;

/// <summary>A borrowed JNI environment restricted to its creating thread and scope.</summary>
public sealed unsafe class JavaEnvironment : IDisposable
{
    private readonly JNINativeInterface_** _environment;
    private readonly int _threadIdentifier = Environment.CurrentManagedThreadId;
    private bool _disposed;

    public JavaEnvironment(nint environment)
    {
        ArgumentOutOfRangeException.ThrowIfZero(environment);
        _environment = (JNINativeInterface_**)environment;
    }

    public nint Handle { get { EnsureAccessible(); return (nint)_environment; } }
    public JNINativeInterface_* Functions { get { EnsureAccessible(); return *_environment; } }
    public int Version => Functions->GetVersion(_environment);

    internal void EnsureAccessible()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_threadIdentifier != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("JNI environments and local references cannot cross threads.");
    }

    public void ThrowIfException(string operation)
    {
        if (Functions->ExceptionCheck(_environment) == 0)
            return;
        Functions->ExceptionClear(_environment);
        throw new JavaException(operation);
    }

    private JavaLocalReference Own(_jobject* value, string operation)
    {
        ThrowIfException(operation);
        if (value == null)
            throw new InvalidOperationException($"{operation} returned a null JNI reference.");
        return new JavaLocalReference(this, (nint)value);
    }

    public JavaLocalReference FindClass(string name)
    {
        fixed (byte* encoded = ModifiedUtf8.Encode(name))
            return Own(Functions->FindClass(_environment, encoded), nameof(FindClass));
    }

    public JavaLocalReference NewLocalReference(nint value) => Own(Functions->NewLocalRef(_environment, (_jobject*)value), nameof(NewLocalReference));

    public JavaLocalReference NewString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        fixed (char* characters = value)
            return Own(Functions->NewString(_environment, (ushort*)characters, value.Length), nameof(NewString));
    }

    public string? GetString(nint value)
    {
        EnsureAccessible();
        if (value == 0)
            return null;
        ushort* characters = Functions->GetStringChars(_environment, (_jobject*)value, null);
        ThrowIfException(nameof(GetString));
        if (characters == null)
            throw new InvalidOperationException("GetStringChars returned null.");
        try
        {
            int length = Functions->GetStringLength(_environment, (_jobject*)value);
            return new string((char*)characters, 0, length);
        }
        finally
        {
            Functions->ReleaseStringChars(_environment, (_jobject*)value, characters);
        }
    }

    public nint GetMethod(nint type, string name, string signature, bool isStatic = false)
    {
        ArgumentOutOfRangeException.ThrowIfZero(type);
        fixed (byte* encodedName = ModifiedUtf8.Encode(name))
        fixed (byte* encodedSignature = ModifiedUtf8.Encode(signature))
        {
            _jmethodID* method = isStatic
                ? Functions->GetStaticMethodID(_environment, (_jobject*)type, encodedName, encodedSignature)
                : Functions->GetMethodID(_environment, (_jobject*)type, encodedName, encodedSignature);
            ThrowIfException(nameof(GetMethod));
            if (method == null)
                throw new MissingMethodException(name);
            return (nint)method;
        }
    }

    /// <summary>Calls an object-returning method; a Java null result is represented by an empty reference.</summary>
    public JavaLocalReference CallObject(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, bool isStatic = false)
    {
        ArgumentOutOfRangeException.ThrowIfZero(receiver);
        ArgumentOutOfRangeException.ThrowIfZero(method);
        fixed (jvalue* values = arguments)
        {
            _jobject* result = isStatic
                ? Functions->CallStaticObjectMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values)
                : Functions->CallObjectMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values);
            ThrowIfException(nameof(CallObject));
            return new JavaLocalReference(this, (nint)result);
        }
    }

    public int CallInt(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, bool isStatic = false)
    {
        ArgumentOutOfRangeException.ThrowIfZero(receiver);
        ArgumentOutOfRangeException.ThrowIfZero(method);
        fixed (jvalue* values = arguments)
        {
            int result = isStatic
                ? Functions->CallStaticIntMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values)
                : Functions->CallIntMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values);
            ThrowIfException(nameof(CallInt));
            return result;
        }
    }

    public void CallVoid(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, bool isStatic = false)
    {
        ArgumentOutOfRangeException.ThrowIfZero(receiver);
        ArgumentOutOfRangeException.ThrowIfZero(method);
        fixed (jvalue* values = arguments)
        {
            if (isStatic)
                Functions->CallStaticVoidMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values);
            else
                Functions->CallVoidMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values);
            ThrowIfException(nameof(CallVoid));
        }
    }

    public void RegisterNative(nint type, string name, string signature, nint function)
    {
        ArgumentOutOfRangeException.ThrowIfZero(function);
        fixed (byte* encodedName = ModifiedUtf8.Encode(name))
        fixed (byte* encodedSignature = ModifiedUtf8.Encode(signature))
        {
            JNINativeMethod method = new() { name = encodedName, signature = encodedSignature, fnPtr = (void*)function };
            int result = Functions->RegisterNatives(_environment, (_jobject*)type, &method, 1);
            ThrowIfException(nameof(RegisterNative));
            JavaVirtualMachine.Check(result, nameof(RegisterNative));
        }
    }

    public JavaVirtualMachine GetVirtualMachine()
    {
        JNIInvokeInterface_** machine = null;
        JavaVirtualMachine.Check(Functions->GetJavaVM(_environment, &machine), nameof(GetVirtualMachine));
        return JavaVirtualMachine.Borrow((nint)machine);
    }

    internal JavaGlobalReference Promote(nint value)
    {
        _jobject* global = Functions->NewGlobalRef(_environment, (_jobject*)value);
        ThrowIfException(nameof(Promote));
        if (global == null)
            throw new InvalidOperationException("NewGlobalRef failed; the original local reference is still owned by the caller.");
        return new JavaGlobalReference(GetVirtualMachine(), (nint)global);
    }

    internal void DeleteLocal(nint value) => Functions->DeleteLocalRef(_environment, (_jobject*)value);
    internal void DeleteGlobal(nint value) => Functions->DeleteGlobalRef(_environment, (_jobject*)value);
    public void Dispose()
    {
        if (_disposed)
            return;
        EnsureAccessible();
        _disposed = true;
    }
}

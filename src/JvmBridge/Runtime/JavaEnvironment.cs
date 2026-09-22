using JvmBridge.Native;

namespace JvmBridge.Runtime;

/// <summary>A borrowed JNI environment restricted to its creating thread and scope.</summary>
public sealed unsafe class JavaEnvironment : IDisposable
{
    private readonly JNINativeInterface_** _environment;
    private readonly int _threadIdentifier = Environment.CurrentManagedThreadId;
    private bool _disposed;

    /// <summary>Creates a borrowed environment for the current thread and native scope.</summary>
    /// <param name="environment">The nonzero raw JNI environment pointer.</param>
    public JavaEnvironment(nint environment)
    {
        ArgumentOutOfRangeException.ThrowIfZero(environment);
        _environment = (JNINativeInterface_**)environment;
    }

    /// <summary>Gets the raw JNI environment pointer after validating thread and scope ownership.</summary>
    public nint Handle { get { EnsureAccessible(); return (nint)_environment; } }

    /// <summary>Gets the JNI function table after validating thread and scope ownership.</summary>
    public JNINativeInterface_* Functions { get { EnsureAccessible(); return *_environment; } }

    /// <summary>Gets the JNI version reported by this environment.</summary>
    public int Version => Functions->GetVersion(_environment);

    internal void EnsureAccessible()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_threadIdentifier != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("JNI environments and local references cannot cross threads.");
    }

    /// <summary>Clears and reports a pending Java exception after a checked JNI operation.</summary>
    /// <param name="operation">The managed operation name included in a thrown <see cref="JavaException"/>.</param>
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

    /// <summary>Finds a Java class by its JNI binary name.</summary>
    /// <param name="name">The binary class name encoded as modified UTF-8 for JNI.</param>
    /// <returns>An owned local class reference.</returns>
    public JavaLocalReference FindClass(string name)
    {
        fixed (byte* encoded = ModifiedUtf8.Encode(name))
            return Own(Functions->FindClass(_environment, encoded), nameof(FindClass));
    }

    /// <summary>Creates an owned local reference from another JNI reference.</summary>
    /// <param name="value">The JNI reference to duplicate.</param>
    /// <returns>The owned local reference.</returns>
    public JavaLocalReference NewLocalReference(nint value) => Own(Functions->NewLocalRef(_environment, (_jobject*)value), nameof(NewLocalReference));

    /// <summary>Creates a Java string from the exact UTF-16 code units in a managed string.</summary>
    /// <param name="value">The managed UTF-16 string.</param>
    /// <returns>An owned local reference to the Java string.</returns>
    public JavaLocalReference NewString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        fixed (char* characters = value)
            return Own(Functions->NewString(_environment, (ushort*)characters, value.Length), nameof(NewString));
    }

    /// <summary>Copies a Java string into a managed UTF-16 string.</summary>
    /// <param name="value">The Java string reference, or zero for Java <see langword="null"/>.</param>
    /// <returns>The managed string, or <see langword="null"/> for a null Java reference.</returns>
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

    /// <summary>Resolves an instance or static Java method identifier.</summary>
    /// <param name="type">The declaring Java class reference.</param>
    /// <param name="name">The JNI method name.</param>
    /// <param name="signature">The JNI method descriptor.</param>
    /// <param name="isStatic">Whether to resolve a static method.</param>
    /// <returns>The nonzero JNI method identifier.</returns>
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
    /// <param name="receiver">The instance or declaring-class reference.</param>
    /// <param name="method">The resolved JNI method identifier.</param>
    /// <param name="arguments">The typed JNI argument array.</param>
    /// <param name="isStatic">Whether to invoke a static method.</param>
    /// <returns>An owned local reference that may contain a zero handle for Java <see langword="null"/>.</returns>
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

    /// <summary>Calls an integer-returning instance or static Java method.</summary>
    /// <param name="receiver">The instance or declaring-class reference.</param>
    /// <param name="method">The resolved JNI method identifier.</param>
    /// <param name="arguments">The typed JNI argument array.</param>
    /// <param name="isStatic">Whether to invoke a static method.</param>
    /// <returns>The Java integer result.</returns>
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

    /// <summary>Calls a void-returning instance or static Java method.</summary>
    /// <param name="receiver">The instance or declaring-class reference.</param>
    /// <param name="method">The resolved JNI method identifier.</param>
    /// <param name="arguments">The typed JNI argument array.</param>
    /// <param name="isStatic">Whether to invoke a static method.</param>
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

    /// <summary>Registers one unmanaged function as a native implementation of a Java method.</summary>
    /// <param name="type">The declaring Java class reference.</param>
    /// <param name="name">The Java method name.</param>
    /// <param name="signature">The JNI method descriptor.</param>
    /// <param name="function">The unmanaged function pointer.</param>
    public void RegisterNative(nint type, string name, string signature, nint function)
    {
        ArgumentOutOfRangeException.ThrowIfZero(type);
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

    /// <summary>Gets a borrowed wrapper for the JVM that owns this environment.</summary>
    /// <returns>A JVM wrapper that does not destroy the borrowed machine.</returns>
    public JavaVirtualMachine GetVirtualMachine()
    {
        JNIInvokeInterface_** machine = null;
        JavaVirtualMachine.Check(Functions->GetJavaVM(_environment, &machine), nameof(GetVirtualMachine));
        return JavaVirtualMachine.Borrow((nint)machine);
    }

    internal JavaGlobalReference Promote(nint value)
    {
        JavaVirtualMachine machine = GetVirtualMachine();
        _jobject* global = Functions->NewGlobalRef(_environment, (_jobject*)value);
        ThrowIfException(nameof(Promote));
        if (global == null)
            throw new InvalidOperationException("NewGlobalRef failed; the original local reference is still owned by the caller.");
        return new JavaGlobalReference(machine, (nint)global);
    }

    internal void DeleteLocal(nint value) => Functions->DeleteLocalRef(_environment, (_jobject*)value);
    internal void DeleteGlobal(nint value) => Functions->DeleteGlobalRef(_environment, (_jobject*)value);
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        EnsureAccessible();
        _disposed = true;
    }
}

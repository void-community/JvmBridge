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

    /// <summary>Gets the JNI function table after validating thread and scope ownership.</summary>
    public JNINativeInterface_* Functions { get { EnsureAccessible(); return *_environment; } }

    /// <summary>Gets the raw JNI environment pointer after validating thread and scope ownership.</summary>
    public nint Handle { get { EnsureAccessible(); return (nint)_environment; } }

    /// <summary>Gets the JNI version reported by this environment.</summary>
    public int Version => Functions->GetVersion(_environment);

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
        jvalue emptyArgument = default;
        fixed (jvalue* pinnedValues = arguments)
        {
            jvalue* values = arguments.IsEmpty ? &emptyArgument : pinnedValues;
            int result = isStatic
                ? Functions->CallStaticIntMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values)
                : Functions->CallIntMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values);
            ThrowIfException(nameof(CallInt));
            return result;
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
        jvalue emptyArgument = default;
        fixed (jvalue* pinnedValues = arguments)
        {
            jvalue* values = arguments.IsEmpty ? &emptyArgument : pinnedValues;
            _jobject* result = isStatic
                ? Functions->CallStaticObjectMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values)
                : Functions->CallObjectMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values);
            ThrowIfException(nameof(CallObject));
            return new JavaLocalReference(this, (nint)result);
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
        jvalue emptyArgument = default;
        fixed (jvalue* pinnedValues = arguments)
        {
            jvalue* values = arguments.IsEmpty ? &emptyArgument : pinnedValues;
            if (isStatic)
                Functions->CallStaticVoidMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values);
            else
                Functions->CallVoidMethodA(_environment, (_jobject*)receiver, (_jmethodID*)method, values);
            ThrowIfException(nameof(CallVoid));
        }
    }

    /// <summary>Defines a class from JVM class-file bytes in the supplied loader.</summary>
    /// <param name="name">The JNI binary class name (slash-separated).</param>
    /// <param name="loader">The defining loader, or zero for the bootstrap loader.</param>
    /// <param name="bytes">The complete class-file bytes.</param>
    /// <returns>An owned local reference to the defined class.</returns>
    public JavaLocalReference DefineClass(string name, nint loader, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfZero(bytes.Length);
        fixed (byte* encodedName = ModifiedUtf8.Encode(name))
        fixed (byte* pinnedBytes = bytes)
            return Own(Functions->DefineClass(_environment, encodedName, (_jobject*)loader, (sbyte*)pinnedBytes, bytes.Length), nameof(DefineClass));
    }

    private enum PrimitiveKind { Boolean, Byte, Char, Short, Int, Long, Float, Double }

    /// <summary>Reads a boolean field.</summary>
    public bool GetBooleanField(nint receiver, nint field, bool isStatic = false)
    {
        return GetPrimitiveField(receiver, field, PrimitiveKind.Boolean, isStatic).z != 0;
    }

    /// <summary>Reads a signed byte field.</summary>
    public sbyte GetByteField(nint receiver, nint field, bool isStatic = false)
    {
        return GetPrimitiveField(receiver, field, PrimitiveKind.Byte, isStatic).b;
    }

    /// <summary>Reads a UTF-16 code-unit field.</summary>
    public char GetCharField(nint receiver, nint field, bool isStatic = false)
    {
        return (char)GetPrimitiveField(receiver, field, PrimitiveKind.Char, isStatic).c;
    }

    /// <summary>Reads a double field.</summary>
    public double GetDoubleField(nint receiver, nint field, bool isStatic = false)
    {
        return GetPrimitiveField(receiver, field, PrimitiveKind.Double, isStatic).d;
    }

    /// <summary>Resolves an instance or static field by its declaring class, name, and JNI descriptor. The ID does not retain the class.</summary>
    public nint GetField(nint type, string name, string descriptor, bool isStatic = false)
    {
        ArgumentOutOfRangeException.ThrowIfZero(type);
        fixed (byte* encodedName = ModifiedUtf8.Encode(name))
        fixed (byte* encodedDescriptor = ModifiedUtf8.Encode(descriptor))
        {
            _jfieldID* field = isStatic
                ? Functions->GetStaticFieldID(_environment, (_jobject*)type, encodedName, encodedDescriptor)
                : Functions->GetFieldID(_environment, (_jobject*)type, encodedName, encodedDescriptor);
            ThrowIfException(nameof(GetField));
            return field == null ? throw new MissingFieldException(name) : (nint)field;
        }
    }

    /// <summary>Reads a float field.</summary>
    public float GetFloatField(nint receiver, nint field, bool isStatic = false)
    {
        return GetPrimitiveField(receiver, field, PrimitiveKind.Float, isStatic).f;
    }

    /// <summary>Reads an integer field.</summary>
    public int GetIntField(nint receiver, nint field, bool isStatic = false)
    {
        return GetPrimitiveField(receiver, field, PrimitiveKind.Int, isStatic).i;
    }

    /// <summary>Reads a long field.</summary>
    public long GetLongField(nint receiver, nint field, bool isStatic = false)
    {
        return GetPrimitiveField(receiver, field, PrimitiveKind.Long, isStatic).j;
    }

    /// <summary>Gets a disposable local reference to an object's class.</summary>
    public JavaLocalReference GetObjectClass(nint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        return Own(Functions->GetObjectClass(_environment, (_jobject*)value), nameof(GetObjectClass));
    }

    /// <summary>Reads an object field as a disposable local reference; Java null has a zero handle.</summary>
    public JavaLocalReference GetObjectField(nint receiver, nint field, bool isStatic = false)
    {
        ValidateField(receiver, field);
        _jobject* result = isStatic
            ? Functions->GetStaticObjectField(_environment, (_jobject*)receiver, (_jfieldID*)field)
            : Functions->GetObjectField(_environment, (_jobject*)receiver, (_jfieldID*)field);
        ThrowIfException(nameof(GetObjectField));
        return new JavaLocalReference(this, (nint)result);
    }

    /// <summary>Reads a short field.</summary>
    public short GetShortField(nint receiver, nint field, bool isStatic = false)
    {
        return GetPrimitiveField(receiver, field, PrimitiveKind.Short, isStatic).s;
    }

    /// <summary>Gets a disposable local reference to the superclass, or an empty reference for a root class or interface.</summary>
    public JavaLocalReference GetSuperclass(nint type)
    {
        ArgumentOutOfRangeException.ThrowIfZero(type);
        _jobject* result = Functions->GetSuperclass(_environment, (_jobject*)type);
        ThrowIfException(nameof(GetSuperclass));
        return new JavaLocalReference(this, (nint)result);
    }

    /// <summary>Tests whether an object can be assigned to a class; JNI considers Java null assignable to any class.</summary>
    public bool IsInstanceOf(nint value, nint type)
    {
        ArgumentOutOfRangeException.ThrowIfZero(type);
        bool result = Functions->IsInstanceOf(_environment, (_jobject*)value, (_jobject*)type) != 0;
        ThrowIfException(nameof(IsInstanceOf));
        return result;
    }

    /// <summary>Writes a boolean field.</summary>
    public void SetBooleanField(nint receiver, nint field, bool value, bool isStatic = false)
    {
        SetPrimitiveField(receiver, field, new jvalue { z = (byte)(value ? 1 : 0) }, PrimitiveKind.Boolean, isStatic);
    }

    /// <summary>Writes a signed byte field.</summary>
    public void SetByteField(nint receiver, nint field, sbyte value, bool isStatic = false)
    {
        SetPrimitiveField(receiver, field, new jvalue { b = value }, PrimitiveKind.Byte, isStatic);
    }

    /// <summary>Writes a UTF-16 code-unit field.</summary>
    public void SetCharField(nint receiver, nint field, char value, bool isStatic = false)
    {
        SetPrimitiveField(receiver, field, new jvalue { c = value }, PrimitiveKind.Char, isStatic);
    }

    /// <summary>Writes a double field.</summary>
    public void SetDoubleField(nint receiver, nint field, double value, bool isStatic = false)
    {
        SetPrimitiveField(receiver, field, new jvalue { d = value }, PrimitiveKind.Double, isStatic);
    }

    /// <summary>Writes a float field.</summary>
    public void SetFloatField(nint receiver, nint field, float value, bool isStatic = false)
    {
        SetPrimitiveField(receiver, field, new jvalue { f = value }, PrimitiveKind.Float, isStatic);
    }

    /// <summary>Writes an integer field.</summary>
    public void SetIntField(nint receiver, nint field, int value, bool isStatic = false)
    {
        SetPrimitiveField(receiver, field, new jvalue { i = value }, PrimitiveKind.Int, isStatic);
    }

    /// <summary>Writes a long field.</summary>
    public void SetLongField(nint receiver, nint field, long value, bool isStatic = false)
    {
        SetPrimitiveField(receiver, field, new jvalue { j = value }, PrimitiveKind.Long, isStatic);
    }

    /// <summary>Writes an object field; zero represents Java null.</summary>
    public void SetObjectField(nint receiver, nint field, nint value, bool isStatic = false)
    {
        ValidateField(receiver, field);
        if (isStatic)
            Functions->SetStaticObjectField(_environment, (_jobject*)receiver, (_jfieldID*)field, (_jobject*)value);
        else
            Functions->SetObjectField(_environment, (_jobject*)receiver, (_jfieldID*)field, (_jobject*)value);
        ThrowIfException(nameof(SetObjectField));
    }

    /// <summary>Writes a short field.</summary>
    public void SetShortField(nint receiver, nint field, short value, bool isStatic = false)
    {
        SetPrimitiveField(receiver, field, new jvalue { s = value }, PrimitiveKind.Short, isStatic);
    }

    private static void ValidateField(nint receiver, nint field)
    {
        ArgumentOutOfRangeException.ThrowIfZero(receiver);
        ArgumentOutOfRangeException.ThrowIfZero(field);
    }

    private jvalue GetPrimitiveField(nint receiver, nint field, PrimitiveKind kind, bool isStatic)
    {
        ValidateField(receiver, field);
        JNINativeInterface_* functions = Functions;
        _jobject* target = (_jobject*)receiver;
        _jfieldID* identifier = (_jfieldID*)field;
        jvalue result = default;
        if (isStatic)
        {
            switch (kind)
            {
                case PrimitiveKind.Boolean: result.z = functions->GetStaticBooleanField(_environment, target, identifier); break;
                case PrimitiveKind.Byte: result.b = functions->GetStaticByteField(_environment, target, identifier); break;
                case PrimitiveKind.Char: result.c = functions->GetStaticCharField(_environment, target, identifier); break;
                case PrimitiveKind.Short: result.s = functions->GetStaticShortField(_environment, target, identifier); break;
                case PrimitiveKind.Int: result.i = functions->GetStaticIntField(_environment, target, identifier); break;
                case PrimitiveKind.Long: result.j = functions->GetStaticLongField(_environment, target, identifier); break;
                case PrimitiveKind.Float: result.f = functions->GetStaticFloatField(_environment, target, identifier); break;
                case PrimitiveKind.Double: result.d = functions->GetStaticDoubleField(_environment, target, identifier); break;
                default:
                    break;
            }
        }
        else
        {
            switch (kind)
            {
                case PrimitiveKind.Boolean: result.z = functions->GetBooleanField(_environment, target, identifier); break;
                case PrimitiveKind.Byte: result.b = functions->GetByteField(_environment, target, identifier); break;
                case PrimitiveKind.Char: result.c = functions->GetCharField(_environment, target, identifier); break;
                case PrimitiveKind.Short: result.s = functions->GetShortField(_environment, target, identifier); break;
                case PrimitiveKind.Int: result.i = functions->GetIntField(_environment, target, identifier); break;
                case PrimitiveKind.Long: result.j = functions->GetLongField(_environment, target, identifier); break;
                case PrimitiveKind.Float: result.f = functions->GetFloatField(_environment, target, identifier); break;
                case PrimitiveKind.Double: result.d = functions->GetDoubleField(_environment, target, identifier); break;
                default:
                    break;
            }
        }
        ThrowIfException($"Get{kind}Field");
        return result;
    }

    private void SetPrimitiveField(nint receiver, nint field, jvalue value, PrimitiveKind kind, bool isStatic)
    {
        ValidateField(receiver, field);
        JNINativeInterface_* functions = Functions;
        _jobject* target = (_jobject*)receiver;
        _jfieldID* identifier = (_jfieldID*)field;
        if (isStatic)
        {
            switch (kind)
            {
                case PrimitiveKind.Boolean: functions->SetStaticBooleanField(_environment, target, identifier, value.z); break;
                case PrimitiveKind.Byte: functions->SetStaticByteField(_environment, target, identifier, value.b); break;
                case PrimitiveKind.Char: functions->SetStaticCharField(_environment, target, identifier, value.c); break;
                case PrimitiveKind.Short: functions->SetStaticShortField(_environment, target, identifier, value.s); break;
                case PrimitiveKind.Int: functions->SetStaticIntField(_environment, target, identifier, value.i); break;
                case PrimitiveKind.Long: functions->SetStaticLongField(_environment, target, identifier, value.j); break;
                case PrimitiveKind.Float: functions->SetStaticFloatField(_environment, target, identifier, value.f); break;
                case PrimitiveKind.Double: functions->SetStaticDoubleField(_environment, target, identifier, value.d); break;
                default:
                    break;
            }
        }
        else
        {
            switch (kind)
            {
                case PrimitiveKind.Boolean: functions->SetBooleanField(_environment, target, identifier, value.z); break;
                case PrimitiveKind.Byte: functions->SetByteField(_environment, target, identifier, value.b); break;
                case PrimitiveKind.Char: functions->SetCharField(_environment, target, identifier, value.c); break;
                case PrimitiveKind.Short: functions->SetShortField(_environment, target, identifier, value.s); break;
                case PrimitiveKind.Int: functions->SetIntField(_environment, target, identifier, value.i); break;
                case PrimitiveKind.Long: functions->SetLongField(_environment, target, identifier, value.j); break;
                case PrimitiveKind.Float: functions->SetFloatField(_environment, target, identifier, value.f); break;
                case PrimitiveKind.Double: functions->SetDoubleField(_environment, target, identifier, value.d); break;
                default:
                    break;
            }
        }
        ThrowIfException($"Set{kind}Field");
    }
    /// <summary>Calls a boolean-returning instance or static method.</summary>
    public bool CallBoolean(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, bool isStatic = false)
    {
        return CallPrimitive(receiver, method, arguments, PrimitiveKind.Boolean, isStatic).z != 0;
    }

    /// <summary>Calls a signed-byte-returning instance or static method.</summary>
    public sbyte CallByte(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, bool isStatic = false)
    {
        return CallPrimitive(receiver, method, arguments, PrimitiveKind.Byte, isStatic).b;
    }

    /// <summary>Calls a UTF-16-code-unit-returning instance or static method.</summary>
    public char CallChar(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, bool isStatic = false)
    {
        return (char)CallPrimitive(receiver, method, arguments, PrimitiveKind.Char, isStatic).c;
    }

    /// <summary>Calls a double-returning instance or static method.</summary>
    public double CallDouble(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, bool isStatic = false)
    {
        return CallPrimitive(receiver, method, arguments, PrimitiveKind.Double, isStatic).d;
    }

    /// <summary>Calls a float-returning instance or static method.</summary>
    public float CallFloat(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, bool isStatic = false)
    {
        return CallPrimitive(receiver, method, arguments, PrimitiveKind.Float, isStatic).f;
    }

    /// <summary>Calls a long-returning instance or static method.</summary>
    public long CallLong(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, bool isStatic = false)
    {
        return CallPrimitive(receiver, method, arguments, PrimitiveKind.Long, isStatic).j;
    }

    /// <summary>Calls a short-returning instance or static method.</summary>
    public short CallShort(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, bool isStatic = false)
    {
        return CallPrimitive(receiver, method, arguments, PrimitiveKind.Short, isStatic).s;
    }

    /// <summary>Invokes a constructor with a JNI argument array and returns an owned local object reference.</summary>
    public JavaLocalReference NewObject(nint type, nint constructor, ReadOnlySpan<jvalue> arguments)
    {
        ArgumentOutOfRangeException.ThrowIfZero(type);
        ArgumentOutOfRangeException.ThrowIfZero(constructor);
        jvalue emptyArgument = default;
        fixed (jvalue* pinnedValues = arguments)
        {
            jvalue* values = arguments.IsEmpty ? &emptyArgument : pinnedValues;
            return Own(Functions->NewObjectA(_environment, (_jobject*)type, (_jmethodID*)constructor, values), nameof(NewObject));
        }
    }

    private jvalue CallPrimitive(nint receiver, nint method, ReadOnlySpan<jvalue> arguments, PrimitiveKind kind, bool isStatic)
    {
        ArgumentOutOfRangeException.ThrowIfZero(receiver);
        ArgumentOutOfRangeException.ThrowIfZero(method);
        JNINativeInterface_* functions = Functions;
        _jobject* target = (_jobject*)receiver;
        _jmethodID* identifier = (_jmethodID*)method;
        jvalue result = default;
        jvalue emptyArgument = default;
        fixed (jvalue* pinnedValues = arguments)
        {
            jvalue* values = arguments.IsEmpty ? &emptyArgument : pinnedValues;
            if (isStatic)
            {
                switch (kind)
                {
                    case PrimitiveKind.Boolean: result.z = functions->CallStaticBooleanMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Byte: result.b = functions->CallStaticByteMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Char: result.c = functions->CallStaticCharMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Short: result.s = functions->CallStaticShortMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Long: result.j = functions->CallStaticLongMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Float: result.f = functions->CallStaticFloatMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Double: result.d = functions->CallStaticDoubleMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Int:
                        break;
                    default:
                        break;
                }
            }
            else
            {
                switch (kind)
                {
                    case PrimitiveKind.Boolean: result.z = functions->CallBooleanMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Byte: result.b = functions->CallByteMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Char: result.c = functions->CallCharMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Short: result.s = functions->CallShortMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Long: result.j = functions->CallLongMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Float: result.f = functions->CallFloatMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Double: result.d = functions->CallDoubleMethodA(_environment, target, identifier, values); break;
                    case PrimitiveKind.Int:
                        break;
                    default:
                        break;
                }
            }
        }
        ThrowIfException($"Call{kind}");
        return result;
    }
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        EnsureAccessible();
        _disposed = true;
    }

    /// <summary>Finds a Java class by its JNI binary name.</summary>
    /// <param name="name">The binary class name encoded as modified UTF-8 for JNI.</param>
    /// <returns>An owned local class reference.</returns>
    public JavaLocalReference FindClass(string name)
    {
        fixed (byte* encoded = ModifiedUtf8.Encode(name))
            return Own(Functions->FindClass(_environment, encoded), nameof(FindClass));
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
            return method == null ? throw new MissingMethodException(name) : (nint)method;
        }
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
            throw new InvalidOperationException(message: "GetStringChars returned null.");
        try
        {
            int length = Functions->GetStringLength(_environment, (_jobject*)value);
            return new string((char*)characters, startIndex: 0, length);
        }
        finally
        {
            Functions->ReleaseStringChars(_environment, (_jobject*)value, characters);
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

    /// <summary>Compares two JNI references by Java object identity, including zero for Java <see langword="null"/>.</summary>
    /// <param name="first">A reference valid in this environment, or zero.</param>
    /// <param name="second">Another reference valid in this environment, or zero.</param>
    /// <returns>Whether both references identify the same Java object.</returns>
    public bool IsSameObject(nint first, nint second)
    {
        bool same = Functions->IsSameObject(_environment, (_jobject*)first, (_jobject*)second) != 0;
        ThrowIfException(nameof(IsSameObject));
        return same;
    }

    /// <summary>Creates an owned weak global reference that does not keep its referent alive.</summary>
    /// <param name="value">A nonzero JNI reference valid in this environment.</param>
    /// <returns>A weak reference to dispose while its JVM is alive.</returns>
    public JavaWeakGlobalReference NewWeakGlobalReference(nint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        JavaVirtualMachine machine = GetVirtualMachine();
        _jobject* weak = Functions->NewWeakGlobalRef(_environment, (_jobject*)value);
        ThrowIfException(nameof(NewWeakGlobalReference));
        return weak == null
            ? throw new InvalidOperationException("NewWeakGlobalRef returned a null JNI reference.")
            : new JavaWeakGlobalReference(machine, (nint)weak);
    }

    /// <summary>Creates an owned local reference from another JNI reference.</summary>
    /// <param name="value">The JNI reference to duplicate.</param>
    /// <returns>The owned local reference, empty when the supplied reference is Java null.</returns>
    public JavaLocalReference NewLocalReference(nint value)
    {
        _jobject* result = Functions->NewLocalRef(_environment, (_jobject*)value);
        ThrowIfException(nameof(NewLocalReference));
        if (result == null && value != 0)
            throw new InvalidOperationException("NewLocalReference returned a null JNI reference.");
        return new JavaLocalReference(this, (nint)result);
    }

    /// <summary>Creates a Java string from the exact UTF-16 code units in a managed string.</summary>
    /// <param name="value">The managed UTF-16 string.</param>
    /// <returns>An owned local reference to the Java string.</returns>
    public JavaLocalReference NewString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        fixed (char* characters = value)
            return Own(Functions->NewString(_environment, (ushort*)characters, value.Length), nameof(NewString));
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

    /// <summary>Clears a pending Java exception and reports a best-effort managed diagnostic snapshot.</summary>
    /// <param name="operation">The managed operation name included in a thrown <see cref="JavaException"/>.</param>
    public void ThrowIfException(string operation)
    {
        JNINativeInterface_* functions = Functions;
        if (functions->ExceptionCheck(_environment) == 0)
            return;
        _jobject* throwable = functions->ExceptionOccurred == null ? null : functions->ExceptionOccurred(_environment);
        functions->ExceptionClear(_environment);
        string? typeName = null;
        string? message = null;
        string? stackTrace = null;
        try
        {
            if (throwable != null)
                (typeName, message, stackTrace) = JavaExceptionDiagnostics.Capture(_environment, functions, throwable);
        }
        catch (Exception exception) when (JavaExceptionDiagnostics.ContainCaptureFailure(exception))
        {
        }
        finally
        {
            if (functions->ExceptionCheck(_environment) != 0)
                functions->ExceptionClear(_environment);
            if (throwable != null)
                functions->DeleteLocalRef(_environment, throwable);
        }
        throw new JavaException(operation, typeName, message, stackTrace);
    }

    internal void DeleteGlobal(nint value)
    {
        Functions->DeleteGlobalRef(_environment, (_jobject*)value);
    }

    internal void DeleteWeakGlobal(nint value)
    {
        Functions->DeleteWeakGlobalRef(_environment, (_jobject*)value);
    }

    internal void DeleteLocal(nint value)
    {
        Functions->DeleteLocalRef(_environment, (_jobject*)value);
    }

    internal void EnsureAccessible()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_threadIdentifier != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException(message: "JNI environments and local references cannot cross threads.");
    }

    internal JavaGlobalReference Promote(nint value)
    {
        JavaVirtualMachine machine = GetVirtualMachine();
        _jobject* global = Functions->NewGlobalRef(_environment, (_jobject*)value);
        ThrowIfException(nameof(Promote));
        return global == null
            ? throw new InvalidOperationException(message: "NewGlobalRef failed; the original local reference is still owned by the caller.")
            : new JavaGlobalReference(machine, (nint)global);
    }

    internal JavaLocalReference? TryPromoteWeak(nint value)
    {
        _jobject* local = Functions->NewLocalRef(_environment, (_jobject*)value);
        ThrowIfException(nameof(TryPromoteWeak));
        return local == null ? null : new JavaLocalReference(this, (nint)local);
    }

    private JavaLocalReference Own(_jobject* value, string operation)
    {
        ThrowIfException(operation);
        return value == null
            ? throw new InvalidOperationException($"{operation} returned a null JNI reference.")
            : new JavaLocalReference(this, (nint)value);
    }
}

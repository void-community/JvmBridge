using System.Buffers.Binary;

using JvmBridge.ClassFiles.Code;

namespace JvmBridge.ClassFiles;

/// <summary>Writes small classes made of static methods. Straight-line code needs no stack map frames, so only code without branches is supported for version 51 and later.</summary>
public sealed class ClassFileWriter : IConstantPool
{
    /// <summary>Class access flags <c>ACC_PUBLIC | ACC_SUPER</c>.</summary>
    public const int PublicSuper = 0x0021;

    /// <summary>Method access flags <c>ACC_PUBLIC | ACC_STATIC</c>.</summary>
    public const int PublicStatic = 0x0009;

    /// <summary>Method access flags <c>ACC_PUBLIC | ACC_STATIC | ACC_NATIVE</c>.</summary>
    public const int PublicStaticNative = 0x0109;

    private readonly ConstantPoolBuilder _pool = new();
    private readonly Dictionary<(string Name, string Descriptor), int> _declared = [];
    private readonly List<int[]> _bootstrapMethods = [];
    private readonly List<(string Inner, byte[] Entry)> _innerClasses = [];
    private readonly List<(int Access, int Name, int Descriptor)> _fields = [];
    private readonly List<(int Access, int Name, int Descriptor, int MaxStack, int MaxLocals, byte[]? Code)> _methods = [];
    private readonly int _majorVersion;
    private readonly int _accessFlags;
    private readonly int _thisClass;
    private readonly int _superClass;

    /// <summary>Starts an empty class with no fields or methods, to add members to and write out with <see cref="ToArray"/>.</summary>
    /// <param name="name">The internal name of the class, for example <c>pkg/Helper</c>.</param>
    /// <param name="superName">The internal name of the superclass.</param>
    /// <param name="accessFlags">The class access flags.</param>
    /// <param name="majorVersion">The class file major version; 52 is Java 8.</param>
    public ClassFileWriter(string name, string superName = "java/lang/Object", int accessFlags = PublicSuper, int majorVersion = 52)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(superName);
        ArgumentOutOfRangeException.ThrowIfNegative(accessFlags);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(accessFlags, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(majorVersion, 45);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(majorVersion, ushort.MaxValue);
        Name = name;
        _accessFlags = accessFlags;
        _majorVersion = majorVersion;
        _thisClass = _pool.Class(name);
        _superClass = _pool.Class(superName);
    }

    /// <summary>Gets the internal name of the class being written.</summary>
    public string Name { get; }

    /// <summary>Adds a Utf8 constant, returning the existing index when it was added before.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>The constant pool index.</returns>
    public int AddUtf8(string value) => _pool.Utf8(value);

    /// <summary>Adds a Class constant.</summary>
    /// <param name="name">The internal class name.</param>
    /// <returns>The constant pool index.</returns>
    public int AddClass(string name) => _pool.Class(name);

    /// <summary>Adds a String constant.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>The constant pool index.</returns>
    public int AddString(string value) => _pool.String(value);

    /// <summary>Adds an Integer constant.</summary>
    /// <param name="value">The number the constant pool entry holds.</param>
    /// <returns>The constant pool index.</returns>
    public int AddInteger(int value) => _pool.Integer(value);

    /// <summary>Adds a Float constant.</summary>
    /// <param name="value">The number the constant pool entry holds.</param>
    /// <returns>The constant pool index.</returns>
    public int AddFloat(float value) => _pool.Float(value);

    /// <summary>Adds a Long constant, which occupies two slots.</summary>
    /// <param name="value">The number the constant pool entry holds.</param>
    /// <returns>The constant pool index.</returns>
    public int AddLong(long value) => _pool.Long(value);

    /// <summary>Adds a Double constant, which occupies two slots.</summary>
    /// <param name="value">The number the constant pool entry holds.</param>
    /// <returns>The constant pool index.</returns>
    public int AddDouble(double value) => _pool.Double(value);

    /// <summary>Adds a field reference.</summary>
    /// <param name="owner">The internal name of the owner.</param>
    /// <param name="name">The name the field is declared with.</param>
    /// <param name="descriptor">Its type in JVM notation, for example <c>I</c> or <c>Ljava/lang/String;</c>.</param>
    /// <returns>The constant pool index.</returns>
    public int AddFieldReference(string owner, string name, string descriptor) => _pool.MemberReference(ConstantTag.Fieldref, owner, name, descriptor);

    /// <summary>Adds a method reference of a class.</summary>
    /// <param name="owner">The internal name of the owner.</param>
    /// <param name="name">The name the method is declared with, for example <c>toString</c>.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>(I)Ljava/lang/String;</c>, which tell overloads apart.</param>
    /// <returns>The constant pool index.</returns>
    public int AddMethodReference(string owner, string name, string descriptor) => _pool.MemberReference(ConstantTag.Methodref, owner, name, descriptor);

    /// <summary>Adds a method reference of an interface.</summary>
    /// <param name="owner">The internal name of the interface.</param>
    /// <param name="name">The name the method is declared with, for example <c>toString</c>.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>(I)Ljava/lang/String;</c>, which tell overloads apart.</param>
    /// <returns>The constant pool index.</returns>
    public int AddInterfaceMethodReference(string owner, string name, string descriptor) => _pool.MemberReference(ConstantTag.InterfaceMethodref, owner, name, descriptor);

    /// <summary>Adds a method with a body of straight-line code.</summary>
    /// <param name="name">The name the method is declared with, for example <c>toString</c>.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>(I)Ljava/lang/String;</c>, which tell overloads apart.</param>
    /// <param name="maxStack">The maximum operand stack depth.</param>
    /// <param name="maxLocals">The number of local variable slots, including parameters.</param>
    /// <param name="code">The bytecode. It must not contain switches or exception handlers, nor branches in a class of version 51 or later; <see cref="AddMethod(string, string, Func{CodeBuilder, CodeBuilder}, int)"/> writes it without byte literals.</param>
    /// <param name="accessFlags">The <c>ACC_</c> bits; public and static by default, so that patched code can call it.</param>
    /// <returns>This writer.</returns>
    public ClassFileWriter AddMethod(string name, string descriptor, int maxStack, int maxLocals, ReadOnlySpan<byte> code, int accessFlags = PublicStatic)
    {
        ValidateMethod(name, descriptor, accessFlags);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxStack, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLocals, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegative(maxStack);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLocals);
        if (code.Length is 0 or > ushort.MaxValue)
            throw new ArgumentException("A method body must contain between 1 and 65535 bytes of code.", nameof(code));
        int nameIndex = _pool.Utf8(name);
        int descriptorIndex = _pool.Utf8(descriptor);
        _ = _pool.Utf8("Code");
        _methods.Add((accessFlags, nameIndex, descriptorIndex, maxStack, maxLocals, code.ToArray()));
        _declared[(name, descriptor)] = accessFlags;
        return this;
    }

    /// <summary>
    /// Adds a method whose body a <see cref="CodeBuilder"/> writes, with <c>max_stack</c> and <c>max_locals</c> worked out from it: <c>writer.AddMethod("name", "()Ljava/lang/String;", code =&gt; code.Ldc("text").AReturn())</c>. Branches need
    /// stack map frames from class file version 51, which this writer does not write, so they are refused there: write a helper with branches as a <c>JavaSource</c> item, or in a class of version 50, which every JVM verifies without frames.
    /// </summary>
    /// <param name="name">The name the method is declared with, for example <c>toString</c>.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>(I)Ljava/lang/String;</c>, which tell overloads apart.</param>
    /// <param name="code">Writes the body and returns the builder; the parameters are in the first local variables, after <c>this</c> for an instance method.</param>
    /// <param name="accessFlags">The <c>ACC_</c> bits; public and static by default, so that patched code can call it.</param>
    /// <returns>This writer.</returns>
    /// <exception cref="ArgumentException">The body branches in a class of version 51 or later.</exception>
    /// <exception cref="InvalidOperationException">The body is not consistent: it pops more than the stack holds, has code no path reaches, or branches to a label it never places.</exception>
    public ClassFileWriter AddMethod(string name, string descriptor, Func<CodeBuilder, CodeBuilder> code, int accessFlags = PublicStatic)
    {
        ArgumentNullException.ThrowIfNull(code);
        ValidateMethod(name, descriptor, accessFlags);

        int receiver = (accessFlags & 0x0008) == 0 ? 1 : 0;
        CodeBuilder builder = new(this, Opcodes.CountArgumentSlots(descriptor) + receiver);
        CodeBuilder.RequireSame(builder, code(builder));

        if (builder.HasBranches && _majorVersion >= 51)
            throw new ArgumentException($"{name}{descriptor} branches, which a class file of version {_majorVersion} only allows with stack map frames that this writer does not write.", nameof(code));

        return AddMethod(name, descriptor, builder.MaxStack, builder.MaxLocals, builder.ToArray(), accessFlags);
    }

    /// <summary>Declares a field, with no initial value: an instance field is null or zero until code sets it, as is a static field until the class's code does.</summary>
    /// <param name="name">The name the field is declared with.</param>
    /// <param name="descriptor">Its type in JVM notation, for example <c>I</c> or <c>Ljava/lang/String;</c>.</param>
    /// <param name="accessFlags">The <c>ACC_</c> bits; private by default (<c>0x0002</c>), with <c>0x0008</c> for a static field.</param>
    /// <returns>This writer.</returns>
    public ClassFileWriter AddField(string name, string descriptor, int accessFlags = 0x0002)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(descriptor);
        ArgumentOutOfRangeException.ThrowIfNegative(accessFlags);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(accessFlags, ushort.MaxValue);
        _fields.Add((accessFlags, _pool.Utf8(name), _pool.Utf8(descriptor)));

        return this;
    }

    /// <summary>Adds a <c>public static native</c> method without a body, for registration with RegisterNatives.</summary>
    /// <param name="name">The name the method is declared with, for example <c>toString</c>.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>(I)Ljava/lang/String;</c>, which tell overloads apart.</param>
    /// <param name="accessFlags">The method access flags; it should include <c>ACC_NATIVE</c>.</param>
    /// <returns>This writer.</returns>
    public ClassFileWriter AddNativeMethod(string name, string descriptor, int accessFlags = PublicStaticNative)
    {
        ValidateMethod(name, descriptor, accessFlags);
        _methods.Add((accessFlags, _pool.Utf8(name), _pool.Utf8(descriptor), 0, 0, null));
        _declared[(name, descriptor)] = accessFlags;
        return this;
    }

    /// <summary>Produces the class file.</summary>
    /// <returns>The class file bytes.</returns>
    public byte[] ToArray()
    {
        using MemoryStream stream = new();
        WriteInt32(stream, unchecked((int)0xcafebabe));
        WriteUInt16(stream, 0);
        WriteUInt16(stream, _majorVersion);
        WriteUInt16(stream, _pool.Count);
        foreach (byte[] entry in _pool.Entries)
            stream.Write(entry);
        WriteUInt16(stream, _accessFlags);
        WriteUInt16(stream, _thisClass);
        WriteUInt16(stream, _superClass);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, _fields.Count);

        foreach ((int access, int name, int descriptor) in _fields)
        {
            WriteUInt16(stream, access);
            WriteUInt16(stream, name);
            WriteUInt16(stream, descriptor);
            WriteUInt16(stream, 0);
        }

        WriteUInt16(stream, _methods.Count);
        int codeName = _methods.Any(method => method.Code is not null) ? _pool.Utf8("Code") : 0;
        foreach ((int access, int name, int descriptor, int maxStack, int maxLocals, byte[]? code) in _methods)
        {
            WriteUInt16(stream, access);
            WriteUInt16(stream, name);
            WriteUInt16(stream, descriptor);
            if (code is null)
            {
                WriteUInt16(stream, 0);
                continue;
            }
            WriteUInt16(stream, 1);
            WriteUInt16(stream, codeName);
            WriteInt32(stream, 12 + code.Length);
            WriteUInt16(stream, maxStack);
            WriteUInt16(stream, maxLocals);
            WriteInt32(stream, code.Length);
            stream.Write(code);
            WriteUInt16(stream, 0);
            WriteUInt16(stream, 0);
        }
        WriteAttributes(stream);
        return stream.ToArray();
    }

    int IConstantPool.MajorVersion => _majorVersion;

    int IConstantPool.AddBootstrapMethod(int handle, IReadOnlyList<int> arguments)
    {
        int[] entry = [handle, .. arguments];
        int existing = _bootstrapMethods.FindIndex(candidate => candidate.AsSpan().SequenceEqual(entry));

        if (existing >= 0)
            return existing;

        _ = _pool.Utf8(ClassAttributes.BootstrapMethods);
        _bootstrapMethods.Add(entry);

        return _bootstrapMethods.Count - 1;
    }

    void IConstantPool.AddInnerClass(string innerClass, string outerClass, string simpleName, int accessFlags)
    {
        if (_innerClasses.Exists(candidate => candidate.Inner == innerClass))
            return;

        _ = _pool.Utf8(ClassAttributes.InnerClasses);
        _innerClasses.Add((innerClass, ClassAttributes.InnerClass(_pool.Class(innerClass), _pool.Class(outerClass), _pool.Utf8(simpleName), accessFlags)));
    }

    int IConstantPool.AddInvokeDynamic(int bootstrapMethod, string name, string descriptor) => _pool.InvokeDynamic(bootstrapMethod, name, descriptor);

    int IConstantPool.AddMethodHandle(int referenceKind, ConstantTag referenceTag, string owner, string name, string descriptor) => _pool.MethodHandle(referenceKind, referenceTag, owner, name, descriptor);

    int IConstantPool.AddMethodType(string descriptor) => _pool.MethodType(descriptor);

    bool IConstantPool.IsInterface(string className) => className == Name && (_accessFlags & 0x0200) != 0;

    bool IConstantPool.IsPrivateMethod(string owner, string name, string descriptor) => owner == Name && _declared.TryGetValue((name, descriptor), out int access) && (access & 0x0002) != 0;

    // The class attributes: BootstrapMethods and InnerClasses, when a lambda needs them. Their names were added to the pool with their first entry, since the pool is written first.
    private void WriteAttributes(MemoryStream stream)
    {
        WriteUInt16(stream, (_bootstrapMethods.Count > 0 ? 1 : 0) + (_innerClasses.Count > 0 ? 1 : 0));

        if (_bootstrapMethods.Count > 0)
        {
            byte[][] entries = [.. _bootstrapMethods.Select(static entry => ClassAttributes.BootstrapMethod(entry[0], entry[1..]))];

            WriteAttribute(stream, ClassAttributes.BootstrapMethods, entries);
        }

        if (_innerClasses.Count > 0)
            WriteAttribute(stream, ClassAttributes.InnerClasses, [.. _innerClasses.Select(static inner => inner.Entry)]);
    }

    private void WriteAttribute(MemoryStream stream, string name, byte[][] entries)
    {
        WriteUInt16(stream, _pool.Utf8(name));
        WriteInt32(stream, 2 + entries.Sum(static entry => entry.Length));
        WriteUInt16(stream, entries.Length);

        foreach (byte[] entry in entries)
            stream.Write(entry);
    }

    private void ValidateMethod(string name, string descriptor, int accessFlags)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(descriptor);
        ArgumentOutOfRangeException.ThrowIfNegative(accessFlags);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(accessFlags, ushort.MaxValue);

        if (name.AsSpan().IndexOfAny(".;[/") >= 0 || (name[0] == '<' && name is not ("<init>" or "<clinit>")))
            throw new ArgumentException($"'{name}' is not a valid method name.", nameof(name));

        if (descriptor[0] != '(' || !descriptor.Contains(')'))
            throw new ArgumentException($"'{descriptor}' is not a method descriptor.", nameof(descriptor));

        if (_declared.ContainsKey((name, descriptor)))
            throw new ArgumentException($"The class already declares {name}{descriptor}.", nameof(name));
    }

    private static void WriteUInt16(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, checked((ushort)value));
        stream.Write(buffer);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}

using JvmBridge.Runtime;

namespace JvmBridge.ClassFiles;

/// <summary>A read-only view of a class file: version, flags, hierarchy, members, typed constant pool and decoded code. Stack maps are not interpreted.</summary>
public sealed class JavaClassFile
{
    private const uint Magic = 0xcafebabe;
    private readonly ConstantPoolEntry?[] _constants;

    private JavaClassFile(ConstantPoolEntry?[] constants)
    {
        _constants = constants;
        Name = string.Empty;
        Interfaces = [];
        Fields = [];
        Methods = [];
    }

    /// <summary>Gets the class file minor version.</summary>
    public int MinorVersion { get; private set; }

    /// <summary>Gets the class file major version, for example 52 for Java 8.</summary>
    public int MajorVersion { get; private set; }

    /// <summary>Gets the class access flags.</summary>
    public int AccessFlags { get; private set; }

    /// <summary>Gets the internal name of this class, for example <c>java/lang/Object</c>.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the internal name of the superclass, or <see langword="null"/> for <c>java/lang/Object</c> and modules.</summary>
    public string? SuperName { get; private set; }

    /// <summary>Gets the internal names of the implemented interfaces.</summary>
    public IReadOnlyList<string> Interfaces { get; private set; }

    /// <summary>Gets the declared fields.</summary>
    public IReadOnlyList<ClassMember> Fields { get; private set; }

    /// <summary>Gets the declared methods.</summary>
    public IReadOnlyList<ClassMember> Methods { get; private set; }

    /// <summary>Gets the constant pool addressed by index. Index zero and the slot after a Long or Double are <see langword="null"/>.</summary>
    public IReadOnlyList<ConstantPoolEntry?> ConstantPool => _constants;

    internal int ConstantPoolEnd { get; private set; }

    /// <summary>Gets the offset of the <c>methods_count</c> field.</summary>
    internal int MethodsCountOffset { get; private set; }

    /// <summary>Gets the offset just after the last method.</summary>
    internal int MethodsEnd { get; private set; }

    internal int AccessFlagsOffset { get; private set; }

    /// <summary>Reads a class file, rejecting truncated input, trailing bytes, unknown constant tags and malformed code.</summary>
    /// <param name="bytes">The class file bytes.</param>
    /// <returns>The decoded class file.</returns>
    /// <exception cref="ClassFormatException">The input is not a well-formed class file.</exception>
    public static JavaClassFile Read(ReadOnlySpan<byte> bytes)
    {
        ClassReader reader = new(bytes);
        if (reader.Remaining < 4 || (uint)reader.ReadInt32() != Magic)
            throw new ClassFormatException("The input does not start with the class file magic number 0xCAFEBABE.");
        int minor = reader.ReadUInt16();
        int major = reader.ReadUInt16();
        int count = reader.ReadUInt16();
        if (count == 0)
            throw new ClassFormatException("The constant pool count must be at least 1.");
        ConstantPoolEntry?[] constants = new ConstantPoolEntry?[count];
        for (int index = 1; index < count; index++)
        {
            int offset = reader.Position;
            ConstantPoolEntry entry = ReadConstant(ref reader, offset);
            constants[index] = entry with { Index = index, Offset = offset, Length = reader.Position - offset };
            if (entry.Tag is ConstantTag.Long or ConstantTag.Double)
            {
                index++;
                if (index >= count)
                    throw new ClassFormatException($"The Long or Double constant at offset {offset} does not fit in the constant pool.");
            }
        }
        JavaClassFile file = new(constants) { MinorVersion = minor, MajorVersion = major, ConstantPoolEnd = reader.Position, AccessFlagsOffset = reader.Position };
        file.AccessFlags = reader.ReadUInt16();
        file.Name = file.GetClassName(reader.ReadUInt16());
        int superIndex = reader.ReadUInt16();
        file.SuperName = superIndex == 0 ? null : file.GetClassName(superIndex);
        string[] interfaces = new string[reader.ReadUInt16()];
        for (int index = 0; index < interfaces.Length; index++)
            interfaces[index] = file.GetClassName(reader.ReadUInt16());
        file.Interfaces = interfaces;
        file.Fields = file.ReadMembers(ref reader, isMethod: false);
        file.Methods = file.ReadMembers(ref reader, isMethod: true);
        file.SkipAttributes(ref reader);
        if (reader.Remaining != 0)
            throw new ClassFormatException($"The class file has {reader.Remaining} unexpected trailing bytes at offset {reader.Position}.");
        return file;
    }

    /// <summary>Gets a Utf8 constant.</summary>
    /// <param name="index">The constant pool index.</param>
    /// <returns>The decoded string.</returns>
    public string GetUtf8(int index) => GetEntry<Utf8Entry>(index).Value;

    /// <summary>Gets the internal name of a Class constant.</summary>
    /// <param name="index">The constant pool index.</param>
    /// <returns>The internal class name.</returns>
    public string GetClassName(int index) => GetUtf8(GetEntry<ClassEntry>(index).NameIndex);

    /// <summary>Resolves a field, method or interface method reference.</summary>
    /// <param name="index">The constant pool index.</param>
    /// <returns>The resolved reference.</returns>
    public MemberReference GetMemberReference(int index)
    {
        MemberReferenceEntry entry = GetEntry<MemberReferenceEntry>(index);
        NameAndTypeEntry nameAndType = GetEntry<NameAndTypeEntry>(entry.NameAndTypeIndex);
        return new MemberReference(entry.Kind, GetClassName(entry.ClassIndex), GetUtf8(nameAndType.NameIndex), GetUtf8(nameAndType.DescriptorIndex));
    }

    /// <summary>Finds a declared method.</summary>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor, or <see langword="null"/> to accept any overload.</param>
    /// <returns>The first matching method, or <see langword="null"/>.</returns>
    public ClassMember? FindMethod(string name, string? descriptor = null) => Methods.FirstOrDefault(method => method.Name == name && (descriptor is null || method.Descriptor == descriptor));

    internal T GetEntry<T>(int index)
        where T : ConstantPoolEntry
    {
        if (index <= 0 || index >= _constants.Length)
            throw new ClassFormatException($"Constant pool index {index} is out of range 1..{_constants.Length - 1}.");
        return _constants[index] as T ?? throw new ClassFormatException($"Constant pool index {index} is {_constants[index]?.Tag.ToString() ?? "unusable"}, expected {typeof(T).Name}.");
    }

    private static ConstantPoolEntry ReadConstant(ref ClassReader reader, int offset)
    {
        ConstantTag tag = (ConstantTag)reader.ReadByte();
        switch (tag)
        {
            case ConstantTag.Utf8:
                ReadOnlySpan<byte> payload = reader.ReadBytes(reader.ReadUInt16());
                try { return new Utf8Entry(ModifiedUtf8.Decode(payload)); }
                catch (FormatException exception) { throw new ClassFormatException($"The Utf8 constant at offset {offset} is not valid modified UTF-8.", exception); }
            case ConstantTag.Integer:
                return new IntegerEntry(reader.ReadInt32());
            case ConstantTag.Float:
                return new FloatEntry(BitConverter.Int32BitsToSingle(reader.ReadInt32()));
            case ConstantTag.Long:
                return new LongEntry(reader.ReadInt64());
            case ConstantTag.Double:
                return new DoubleEntry(BitConverter.Int64BitsToDouble(reader.ReadInt64()));
            case ConstantTag.Class:
                return new ClassEntry(reader.ReadUInt16());
            case ConstantTag.String:
                return new StringEntry(reader.ReadUInt16());
            case ConstantTag.Fieldref or ConstantTag.Methodref or ConstantTag.InterfaceMethodref:
                return new MemberReferenceEntry(tag, reader.ReadUInt16(), reader.ReadUInt16());
            case ConstantTag.NameAndType:
                return new NameAndTypeEntry(reader.ReadUInt16(), reader.ReadUInt16());
            case ConstantTag.MethodHandle:
                return new MethodHandleEntry(reader.ReadByte(), reader.ReadUInt16());
            case ConstantTag.MethodType:
                return new MethodTypeEntry(reader.ReadUInt16());
            case ConstantTag.Dynamic or ConstantTag.InvokeDynamic:
                return new DynamicEntry(tag, reader.ReadUInt16(), reader.ReadUInt16());
            case ConstantTag.Module:
                return new ModuleEntry(reader.ReadUInt16());
            case ConstantTag.Package:
                return new PackageEntry(reader.ReadUInt16());
            default:
                throw new ClassFormatException($"Unknown constant pool tag {(byte)tag} at offset {offset}.");
        }
    }

    private ClassMember[] ReadMembers(ref ClassReader reader, bool isMethod)
    {
        if (isMethod)
            MethodsCountOffset = reader.Position;
        ClassMember[] members = new ClassMember[reader.ReadUInt16()];
        for (int index = 0; index < members.Length; index++)
        {
            int accessOffset = reader.Position;
            int codeStart = -1;
            int codeEnd = -1;
            int access = reader.ReadUInt16();
            string name = GetUtf8(reader.ReadUInt16());
            string descriptor = GetUtf8(reader.ReadUInt16());
            CodeAttribute? code = null;
            int attributes = reader.ReadUInt16();
            for (int attribute = 0; attribute < attributes; attribute++)
            {
                int attributeStart = reader.Position;
                string attributeName = GetUtf8(reader.ReadUInt16());
                int length = reader.ReadInt32();
                if (length < 0)
                    throw new ClassFormatException($"Attribute {attributeName} of {name} has a negative length.");
                if (isMethod && attributeName == "Code")
                {
                    int end = reader.Position + length;
                    code = ReadCode(ref reader, name, end);
                    if (reader.Position != end)
                        throw new ClassFormatException($"The Code attribute of {name}{descriptor} has a wrong length.");
                    codeStart = attributeStart;
                    codeEnd = end;
                }
                else
                    reader.Skip(length);
            }
            members[index] = new ClassMember(name, descriptor, access, accessOffset, code, reader.Position, codeStart, codeEnd);
        }
        if (isMethod)
            MethodsEnd = reader.Position;
        return members;
    }

    private CodeAttribute ReadCode(ref ClassReader reader, string method, int end)
    {
        int maxStack = reader.ReadUInt16();
        int maxLocals = reader.ReadUInt16();
        int length = reader.ReadInt32();
        if (length is <= 0 or > 65535)
            throw new ClassFormatException($"The code of {method} has invalid length {length}.");
        int codeOffset = reader.Position;
        ReadOnlySpan<byte> code = reader.ReadBytes(length);
        List<Instruction> instructions = [];
        for (int offset = 0; offset < code.Length;)
        {
            int size = Opcodes.GetLength(code, offset);
            instructions.Add(new Instruction(offset, code[offset], size, Opcodes.GetConstantIndex(code, offset)));
            offset += size;
        }
        reader.Skip(reader.ReadUInt16() * 8);
        SkipAttributes(ref reader);
        if (reader.Position > end)
            throw new ClassFormatException($"The Code attribute of {method} overruns its declared length.");
        return new CodeAttribute(maxStack, maxLocals, codeOffset, length, instructions);
    }

    private void SkipAttributes(ref ClassReader reader)
    {
        int attributes = reader.ReadUInt16();
        for (int attribute = 0; attribute < attributes; attribute++)
        {
            _ = GetUtf8(reader.ReadUInt16());
            int length = reader.ReadInt32();
            if (length < 0)
                throw new ClassFormatException($"An attribute at offset {reader.Position} has a negative length.");
            reader.Skip(length);
        }
    }
}

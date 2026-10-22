namespace JvmBridge.ClassFiles;

/// <summary>The kind of a class-file constant pool entry (JVMS 4.4).</summary>
public enum ConstantTag : byte
{
    /// <summary>A modified UTF-8 string.</summary>
    Utf8 = 1,

    /// <summary>A 32-bit integer.</summary>
    Integer = 3,

    /// <summary>A 32-bit float.</summary>
    Float = 4,

    /// <summary>A 64-bit integer that occupies two slots.</summary>
    Long = 5,

    /// <summary>A 64-bit float that occupies two slots.</summary>
    Double = 6,

    /// <summary>A class or interface.</summary>
    Class = 7,

    /// <summary>A string literal.</summary>
    String = 8,

    /// <summary>A field reference.</summary>
    Fieldref = 9,

    /// <summary>A method reference of a class.</summary>
    Methodref = 10,

    /// <summary>A method reference of an interface.</summary>
    InterfaceMethodref = 11,

    /// <summary>A member name and descriptor.</summary>
    NameAndType = 12,

    /// <summary>A method handle.</summary>
    MethodHandle = 15,

    /// <summary>A method type.</summary>
    MethodType = 16,

    /// <summary>A dynamically computed constant.</summary>
    Dynamic = 17,

    /// <summary>A dynamically bound call site.</summary>
    InvokeDynamic = 18,

    /// <summary>A module.</summary>
    Module = 19,

    /// <summary>A package.</summary>
    Package = 20,
}

/// <summary>A typed constant pool entry. Indices and offsets describe the class file the entry was read from.</summary>
/// <param name="Tag">The entry kind.</param>
public abstract record ConstantPoolEntry(ConstantTag Tag)
{
    /// <summary>Gets the constant pool index of this entry.</summary>
    public int Index { get; internal init; }

    internal int Offset { get; init; }

    internal int Length { get; init; }
}

/// <summary>A modified UTF-8 string constant.</summary>
/// <param name="Value">The decoded UTF-16 value.</param>
public sealed record Utf8Entry(string Value) : ConstantPoolEntry(ConstantTag.Utf8);

/// <summary>A 32-bit integer constant.</summary>
/// <param name="Value">The integer value.</param>
public sealed record IntegerEntry(int Value) : ConstantPoolEntry(ConstantTag.Integer);

/// <summary>A 32-bit float constant.</summary>
/// <param name="Value">The float value.</param>
public sealed record FloatEntry(float Value) : ConstantPoolEntry(ConstantTag.Float);

/// <summary>A 64-bit integer constant. The following index is unusable.</summary>
/// <param name="Value">The integer value.</param>
public sealed record LongEntry(long Value) : ConstantPoolEntry(ConstantTag.Long);

/// <summary>A 64-bit float constant. The following index is unusable.</summary>
/// <param name="Value">The float value.</param>
public sealed record DoubleEntry(double Value) : ConstantPoolEntry(ConstantTag.Double);

/// <summary>A class or interface constant.</summary>
/// <param name="NameIndex">The index of the Utf8 internal name.</param>
public sealed record ClassEntry(int NameIndex) : ConstantPoolEntry(ConstantTag.Class);

/// <summary>A string literal constant.</summary>
/// <param name="StringIndex">The index of the Utf8 value.</param>
public sealed record StringEntry(int StringIndex) : ConstantPoolEntry(ConstantTag.String);

/// <summary>A field, method or interface method reference.</summary>
/// <param name="Kind">Fieldref, Methodref or InterfaceMethodref.</param>
/// <param name="ClassIndex">The index of the owner Class entry.</param>
/// <param name="NameAndTypeIndex">The index of the NameAndType entry.</param>
public sealed record MemberReferenceEntry(ConstantTag Kind, int ClassIndex, int NameAndTypeIndex) : ConstantPoolEntry(Kind);

/// <summary>A member name and descriptor.</summary>
/// <param name="NameIndex">The index of the Utf8 name.</param>
/// <param name="DescriptorIndex">The index of the Utf8 descriptor.</param>
public sealed record NameAndTypeEntry(int NameIndex, int DescriptorIndex) : ConstantPoolEntry(ConstantTag.NameAndType);

/// <summary>A method handle constant.</summary>
/// <param name="ReferenceKind">The handle kind (1 to 9).</param>
/// <param name="ReferenceIndex">The index of the referenced member entry.</param>
public sealed record MethodHandleEntry(int ReferenceKind, int ReferenceIndex) : ConstantPoolEntry(ConstantTag.MethodHandle);

/// <summary>A method type constant.</summary>
/// <param name="DescriptorIndex">The index of the Utf8 descriptor.</param>
public sealed record MethodTypeEntry(int DescriptorIndex) : ConstantPoolEntry(ConstantTag.MethodType);

/// <summary>A dynamically computed constant or call site.</summary>
/// <param name="Kind">Dynamic or InvokeDynamic.</param>
/// <param name="BootstrapMethodIndex">The index into the BootstrapMethods attribute.</param>
/// <param name="NameAndTypeIndex">The index of the NameAndType entry.</param>
public sealed record DynamicEntry(ConstantTag Kind, int BootstrapMethodIndex, int NameAndTypeIndex) : ConstantPoolEntry(Kind);

/// <summary>A module constant.</summary>
/// <param name="NameIndex">The index of the Utf8 name.</param>
public sealed record ModuleEntry(int NameIndex) : ConstantPoolEntry(ConstantTag.Module);

/// <summary>A package constant.</summary>
/// <param name="NameIndex">The index of the Utf8 name.</param>
public sealed record PackageEntry(int NameIndex) : ConstantPoolEntry(ConstantTag.Package);

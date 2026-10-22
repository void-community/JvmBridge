namespace JvmBridge.ClassFiles;

/// <summary>A deduplicating constant pool for new classes. Every constant is added once, in first-use order.</summary>
internal sealed class ConstantPoolBuilder
{
    private readonly List<byte[]> _entries = [];
    private readonly Dictionary<(ConstantTag Tag, string Text, long First, long Second), int> _indices = [];

    internal int Count { get; private set; } = 1;

    internal int Utf8(string value) => Intern((ConstantTag.Utf8, value, 0, 0), () => ConstantEncoding.Utf8(value), slots: 1);

    internal int Integer(int value) => Intern((ConstantTag.Integer, string.Empty, value, 0), () => ConstantEncoding.Integer(value), slots: 1);

    internal int Float(float value) => Intern((ConstantTag.Float, string.Empty, BitConverter.SingleToInt32Bits(value), 0), () => ConstantEncoding.Float(value), slots: 1);

    internal int Long(long value) => Intern((ConstantTag.Long, string.Empty, value, 0), () => ConstantEncoding.Long(value), slots: 2);

    internal int Double(double value) => Intern((ConstantTag.Double, string.Empty, BitConverter.DoubleToInt64Bits(value), 0), () => ConstantEncoding.Double(value), slots: 2);

    internal int Class(string name)
    {
        int utf8 = Utf8(name);
        return Intern((ConstantTag.Class, string.Empty, utf8, 0), () => ConstantEncoding.Reference(ConstantTag.Class, utf8), slots: 1);
    }

    internal int String(string value)
    {
        int utf8 = Utf8(value);
        return Intern((ConstantTag.String, string.Empty, utf8, 0), () => ConstantEncoding.Reference(ConstantTag.String, utf8), slots: 1);
    }

    internal int NameAndType(string name, string descriptor)
    {
        int nameIndex = Utf8(name);
        int descriptorIndex = Utf8(descriptor);
        return Intern((ConstantTag.NameAndType, string.Empty, nameIndex, descriptorIndex), () => ConstantEncoding.Reference(ConstantTag.NameAndType, nameIndex, descriptorIndex), slots: 1);
    }

    internal int MemberReference(ConstantTag kind, string owner, string name, string descriptor)
    {
        int classIndex = Class(owner);
        int nameAndType = NameAndType(name, descriptor);
        return Intern((kind, string.Empty, classIndex, nameAndType), () => ConstantEncoding.Reference(kind, classIndex, nameAndType), slots: 1);
    }

    internal IReadOnlyList<byte[]> Entries => _entries;

    private int Intern((ConstantTag Tag, string Text, long First, long Second) key, Func<byte[]> encode, int slots)
    {
        if (_indices.TryGetValue(key, out int existing))
            return existing;
        if (Count + slots > ushort.MaxValue)
            throw new InvalidOperationException("The constant pool is full (65535 slots).");
        int index = Count;
        _entries.Add(encode());
        _indices.Add(key, index);
        Count += slots;
        return index;
    }
}

using System.Buffers.Binary;

namespace JvmBridge.ClassFiles;

/// <summary>Where one of a class's own attributes lies in the class file, from its name index to the end of its data.</summary>
/// <param name="Name">The attribute name, for example <c>BootstrapMethods</c>.</param>
/// <param name="Start">The offset of the attribute's name index.</param>
/// <param name="End">The offset just after the attribute's data.</param>
internal readonly record struct ClassAttributeRange(string Name, int Start, int End)
{
    /// <summary>Gets the offset of the attribute's data, after the name index and the length.</summary>
    internal int DataStart => Start + 6;
}

/// <summary>
/// Adds entries to the class attributes that hold a count and a list, <c>BootstrapMethods</c> and <c>InnerClasses</c>, and creates the attribute when the class has none. The class attributes come last in a class file, so everything before them keeps its offset.
/// </summary>
internal static class ClassAttributes
{
    internal const string BootstrapMethods = "BootstrapMethods";

    internal const string InnerClasses = "InnerClasses";

    private const int InnerClassSize = 8;

    /// <summary>Encodes a <c>BootstrapMethods</c> entry: the method handle of the bootstrap method and its static arguments.</summary>
    internal static byte[] BootstrapMethod(int handle, IReadOnlyList<int> arguments)
    {
        byte[] entry = new byte[4 + (2 * arguments.Count)];

        BinaryPrimitives.WriteUInt16BigEndian(entry, checked((ushort)handle));
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(start: 2), checked((ushort)arguments.Count));

        for (int index = 0; index < arguments.Count; index++)
            BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(4 + (2 * index)), checked((ushort)arguments[index]));

        return entry;
    }

    /// <summary>Encodes an <c>InnerClasses</c> entry.</summary>
    internal static byte[] InnerClass(int inner, int outer, int simpleName, int accessFlags)
    {
        byte[] entry = new byte[InnerClassSize];

        BinaryPrimitives.WriteUInt16BigEndian(entry, checked((ushort)inner));
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(start: 2), checked((ushort)outer));
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(start: 4), checked((ushort)simpleName));
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(start: 6), checked((ushort)accessFlags));

        return entry;
    }

    /// <summary>Gets the number of entries in a class attribute, zero when the class has none.</summary>
    internal static int Count(ReadOnlySpan<byte> bytes, JavaClassFile model, string name) => Find(model, name) is { } range ? BinaryPrimitives.ReadUInt16BigEndian(bytes[range.DataStart..]) : 0;

    /// <summary>Finds a <c>BootstrapMethods</c> entry with the same handle and arguments, by constant pool index.</summary>
    internal static int? FindBootstrapMethod(ReadOnlySpan<byte> bytes, JavaClassFile model, int handle, IReadOnlyList<int> arguments)
    {
        if (Find(model, BootstrapMethods) is not { } range)
            return null;

        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes[range.DataStart..]);
        int at = range.DataStart + 2;

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = bytes[at..];
            int argumentCount = BinaryPrimitives.ReadUInt16BigEndian(entry[2..]);
            bool same = BinaryPrimitives.ReadUInt16BigEndian(entry) == handle && argumentCount == arguments.Count;

            for (int argument = 0; same && argument < argumentCount; argument++)
                same = BinaryPrimitives.ReadUInt16BigEndian(entry[(4 + (2 * argument))..]) == arguments[argument];

            if (same)
                return index;

            at += 4 + (2 * argumentCount);
        }

        return null;
    }

    /// <summary>Tells whether the <c>InnerClasses</c> attribute has an entry whose inner class constant passes a test.</summary>
    internal static bool ListsInnerClass(ReadOnlySpan<byte> bytes, JavaClassFile model, Func<int, bool> isInner)
    {
        if (Find(model, InnerClasses) is not { } range)
            return false;

        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes[range.DataStart..]);

        for (int index = 0; index < count; index++)
        {
            if (isInner(BinaryPrimitives.ReadUInt16BigEndian(bytes[(range.DataStart + 2 + (InnerClassSize * index))..])))
                return true;
        }

        return false;
    }

    /// <summary>Appends an entry to a class attribute, or adds the attribute with that one entry.</summary>
    /// <param name="bytes">The class file.</param>
    /// <param name="model">The class file read from <paramref name="bytes"/>.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="nameIndex">The index of the Utf8 constant that holds the name, which the caller adds first because a new constant moves the attributes.</param>
    /// <param name="entry">The encoded entry.</param>
    /// <returns>The class file with the entry.</returns>
    internal static byte[] Append(ReadOnlySpan<byte> bytes, JavaClassFile model, string name, int nameIndex, byte[] entry)
    {
        if (Find(model, name) is { } range)
        {
            int count = BinaryPrimitives.ReadUInt16BigEndian(bytes[range.DataStart..]);

            if (count == ushort.MaxValue)
                throw new InvalidOperationException($"The {name} attribute is full (65535 entries).");

            byte[] grown = Insert(bytes, range.End, entry);

            BinaryPrimitives.WriteInt32BigEndian(grown.AsSpan(range.Start + 2), BinaryPrimitives.ReadInt32BigEndian(bytes[(range.Start + 2)..]) + entry.Length);
            BinaryPrimitives.WriteUInt16BigEndian(grown.AsSpan(range.DataStart), (ushort)(count + 1));

            return grown;
        }

        byte[] attribute = new byte[8 + entry.Length];

        BinaryPrimitives.WriteUInt16BigEndian(attribute, checked((ushort)nameIndex));
        BinaryPrimitives.WriteInt32BigEndian(attribute.AsSpan(start: 2), 2 + entry.Length);
        BinaryPrimitives.WriteUInt16BigEndian(attribute.AsSpan(start: 6), value: 1);
        entry.CopyTo(attribute, index: 8);

        byte[] created = Insert(bytes, bytes.Length, attribute);
        int attributes = BinaryPrimitives.ReadUInt16BigEndian(bytes[model.AttributesCountOffset..]);

        BinaryPrimitives.WriteUInt16BigEndian(created.AsSpan(model.AttributesCountOffset), checked((ushort)(attributes + 1)));

        return created;
    }

    private static ClassAttributeRange? Find(JavaClassFile model, string name)
    {
        foreach (ClassAttributeRange range in model.Attributes)
        {
            if (range.Name == name)
                return range;
        }

        return null;
    }

    private static byte[] Insert(ReadOnlySpan<byte> bytes, int at, byte[] inserted)
    {
        byte[] result = new byte[bytes.Length + inserted.Length];

        bytes[..at].CopyTo(result);
        inserted.CopyTo(result, at);
        bytes[at..].CopyTo(result.AsSpan(at + inserted.Length));

        return result;
    }
}

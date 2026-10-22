using System.Buffers.Binary;

namespace JvmBridge.ClassFiles;

internal ref struct ClassReader(ReadOnlySpan<byte> bytes)
{
    private readonly ReadOnlySpan<byte> _bytes = bytes;

    internal int Position { get; private set; }

    internal readonly int Remaining => _bytes.Length - Position;

    internal byte ReadByte()
    {
        Require(1);
        return _bytes[Position++];
    }

    internal int ReadUInt16()
    {
        Require(2);
        int value = BinaryPrimitives.ReadUInt16BigEndian(_bytes[Position..]);
        Position += 2;
        return value;
    }

    internal int ReadInt32()
    {
        Require(4);
        int value = BinaryPrimitives.ReadInt32BigEndian(_bytes[Position..]);
        Position += 4;
        return value;
    }

    internal long ReadInt64()
    {
        Require(8);
        long value = BinaryPrimitives.ReadInt64BigEndian(_bytes[Position..]);
        Position += 8;
        return value;
    }

    internal ReadOnlySpan<byte> ReadBytes(int count)
    {
        Require(count);
        ReadOnlySpan<byte> value = _bytes.Slice(Position, count);
        Position += count;
        return value;
    }

    internal void Skip(int count)
    {
        Require(count);
        Position += count;
    }

    private readonly void Require(int count)
    {
        if (count < 0 || count > Remaining)
            throw new ClassFormatException($"Class file is truncated at offset {Position}: {count} more bytes expected, {Remaining} available.");
    }
}

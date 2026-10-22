using System.Buffers.Binary;

using JvmBridge.Runtime;

namespace JvmBridge.ClassFiles;

internal static class ConstantEncoding
{
    internal static byte[] Utf8(string value)
    {
        byte[] payload = ModifiedUtf8.GetBytes(value);
        if (payload.Length > ushort.MaxValue)
            throw new ArgumentException($"The string encodes to {payload.Length} bytes, more than the 65535 a Utf8 constant can hold.", nameof(value));
        byte[] entry = new byte[3 + payload.Length];
        entry[0] = (byte)ConstantTag.Utf8;
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(1), (ushort)payload.Length);
        payload.CopyTo(entry, 3);
        return entry;
    }

    internal static byte[] Integer(int value)
    {
        byte[] entry = new byte[5];
        entry[0] = (byte)ConstantTag.Integer;
        BinaryPrimitives.WriteInt32BigEndian(entry.AsSpan(1), value);
        return entry;
    }

    internal static byte[] Float(float value)
    {
        byte[] entry = new byte[5];
        entry[0] = (byte)ConstantTag.Float;
        BinaryPrimitives.WriteInt32BigEndian(entry.AsSpan(1), BitConverter.SingleToInt32Bits(value));
        return entry;
    }

    internal static byte[] Long(long value)
    {
        byte[] entry = new byte[9];
        entry[0] = (byte)ConstantTag.Long;
        BinaryPrimitives.WriteInt64BigEndian(entry.AsSpan(1), value);
        return entry;
    }

    internal static byte[] Double(double value)
    {
        byte[] entry = new byte[9];
        entry[0] = (byte)ConstantTag.Double;
        BinaryPrimitives.WriteInt64BigEndian(entry.AsSpan(1), BitConverter.DoubleToInt64Bits(value));
        return entry;
    }

    internal static byte[] Reference(ConstantTag tag, int first)
    {
        byte[] entry = new byte[3];
        entry[0] = (byte)tag;
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(1), checked((ushort)first));
        return entry;
    }

    internal static byte[] Reference(ConstantTag tag, int first, int second)
    {
        byte[] entry = new byte[5];
        entry[0] = (byte)tag;
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(1), checked((ushort)first));
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(3), checked((ushort)second));
        return entry;
    }
}

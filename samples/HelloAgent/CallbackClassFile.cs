using System.Buffers.Binary;
using System.Text;

namespace HelloAgent;

internal static class CallbackClassFile
{
    internal const string Name = "jvmbridge/sample/AgentCallbacks";
    private const string Signature = "(Ljava/lang/Object;)V";

    internal static byte[] Generate()
    {
        using MemoryStream stream = new();

        WriteU4(stream, value: 0xcafebabe);
        WriteU2(stream, value: 0);
        WriteU2(stream, value: 52);
        WriteU2(stream, value: 7);
        WriteUtf8(stream, Name);
        WriteClass(stream, name: 1);
        WriteUtf8(stream, value: "java/lang/Object");
        WriteClass(stream, name: 3);
        WriteUtf8(stream, value: "onFrame");
        WriteUtf8(stream, Signature);
        WriteU2(stream, value: 0x21);
        WriteU2(stream, value: 2);
        WriteU2(stream, value: 4);
        WriteU2(stream, value: 0);
        WriteU2(stream, value: 0);
        WriteU2(stream, value: 1);
        WriteU2(stream, value: 0x109);
        WriteU2(stream, value: 5);
        WriteU2(stream, value: 6);
        WriteU2(stream, value: 0);
        WriteU2(stream, value: 0);

        return stream.ToArray();
    }

    internal static byte[] Inject(byte[] original)
    {
        ReadOnlySpan<byte> bytes = original;

        if (ReadU4(bytes, offset: 0) != 0xcafebabe)
            throw new InvalidOperationException(message: "Invalid fixture class file.");

        int constantCount = ReadU2(bytes, offset: 8);
        string?[] strings = new string?[constantCount];
        int offset = 10;

        for (int index = 1; index < constantCount; index++)
        {
            int tag = bytes[offset++];

            switch (tag)
            {
                case 1:
                    {
                        int length = ReadU2(bytes, offset);
                        offset += 2;
                        strings[index] = Encoding.UTF8.GetString(bytes.Slice(offset, length));
                        offset += length;

                        break;
                    }
                case 3 or 4 or 9 or 10 or 11 or 12 or 17 or 18:
                    {
                        offset += 4;

                        break;
                    }
                case 5 or 6:
                    {
                        offset += 8;
                        index++;

                        break;
                    }
                case 7 or 8 or 16 or 19 or 20:
                    {
                        offset += 2;

                        break;
                    }
                case 15:
                    {
                        offset += 3;

                        break;
                    }
                default:
                    throw new InvalidOperationException($"Unsupported fixture constant tag {tag}.");
            }
        }

        int constantEnd = offset;
        offset += 6;
        int interfaceCount = ReadU2(bytes, offset);
        offset += 2 + (interfaceCount * 2);
        int fieldCount = ReadU2(bytes, offset);
        offset += 2;

        for (int index = 0; index < fieldCount; index++)
            offset = SkipMember(bytes, offset);

        int methodCount = ReadU2(bytes, offset);
        offset += 2;
        int codeOffset = -1;

        for (int index = 0; index < methodCount; index++)
        {
            bool target = strings[ReadU2(bytes, offset + 2)] == "probe" && strings[ReadU2(bytes, offset + 4)] == Signature;
            int attributeCount = ReadU2(bytes, offset + 6);
            offset += 8;

            for (int attribute = 0; attribute < attributeCount; attribute++)
            {
                int length = checked((int)ReadU4(bytes, offset + 2));

                if (target && strings[ReadU2(bytes, offset)] == "Code")
                {
                    if (codeOffset >= 0 || ReadU4(bytes, offset + 10) != 6)
                        throw new InvalidOperationException(message: "Fixture probe code changed.");

                    codeOffset = offset + 14;
                }

                offset += 6 + length;
            }
        }

        bool expectedBody = codeOffset >= 0 && bytes[codeOffset] == 0x2a && bytes[codeOffset + 1] == 0xb6
            && bytes[codeOffset + 4] == 0x57 && bytes[codeOffset + 5] == 0xb1;

        if (!expectedBody)
            throw new InvalidOperationException(message: "Expected Java 8 fixture probe body was not found.");

        int methodReference = constantCount + 5;
        byte[] replacement = [.. original];
        replacement[codeOffset + 1] = 0xb8;
        BinaryPrimitives.WriteUInt16BigEndian(replacement.AsSpan(codeOffset + 2), checked((ushort)methodReference));
        replacement[codeOffset + 4] = 0;

        using MemoryStream additions = new();

        WriteUtf8(additions, Name);
        WriteClass(additions, checked((ushort)constantCount));
        WriteUtf8(additions, value: "onFrame");
        WriteUtf8(additions, Signature);
        additions.WriteByte(value: 12);
        WriteU2(additions, checked((ushort)(constantCount + 2)));
        WriteU2(additions, checked((ushort)(constantCount + 3)));
        additions.WriteByte(value: 10);
        WriteU2(additions, checked((ushort)(constantCount + 1)));
        WriteU2(additions, checked((ushort)(constantCount + 4)));

        using MemoryStream result = new();

        result.Write(replacement, offset: 0, count: 8);
        WriteU2(result, checked((ushort)(constantCount + 6)));
        result.Write(replacement, offset: 10, constantEnd - 10);
        additions.WriteTo(result);
        result.Write(replacement, constantEnd, replacement.Length - constantEnd);

        return result.ToArray();
    }

    private static ushort ReadU2(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
    }

    private static uint ReadU4(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
    }

    private static int SkipMember(ReadOnlySpan<byte> bytes, int offset)
    {
        int attributeCount = ReadU2(bytes, offset + 6);
        offset += 8;

        for (int index = 0; index < attributeCount; index++)
            offset += 6 + checked((int)ReadU4(bytes, offset + 2));

        return offset;
    }

    private static void WriteClass(Stream stream, ushort name)
    {
        stream.WriteByte(value: 7);
        WriteU2(stream, name);
    }

    private static void WriteU2(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteU4(Stream stream, uint value)
    {
        WriteU2(stream, (ushort)(value >> 16));
        WriteU2(stream, (ushort)value);
    }

    private static void WriteUtf8(Stream stream, string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        stream.WriteByte(value: 1);
        WriteU2(stream, checked((ushort)encoded.Length));
        stream.Write(encoded);
    }
}

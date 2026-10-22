using System.Buffers.Binary;
using System.Text;

namespace JvmBridge.Tasks;

/// <summary>
/// Reads the access flags and the methods out of a class file, and nothing else: the constant pool is walked to find the names, and fields, attributes and code are skipped by length. It exists because the task has to run
/// inside MSBuild with no reference to the JvmBridge library.
/// </summary>
internal static class ClassSummaryReader
{
    private const int ClassFileMagic = unchecked((int)0xCAFEBABE);
    private const int Utf8Tag = 1;

    /// <summary>Reads a summary.</summary>
    /// <param name="bytes">The class-file bytes.</param>
    /// <returns>The summary.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a well-formed class file.</exception>
    internal static ClassSummary Read(byte[] bytes)
    {
        int position = 0;

        if (Int32(bytes, ref position) != ClassFileMagic)
            throw new InvalidDataException(message: "The bytes are not a class file: the magic number is wrong.");

        position += 4;
        int count = UInt16(bytes, ref position);
        string?[] utf8 = new string?[count];

        for (int index = 1; index < count; index++)
        {
            int tag = Byte(bytes, ref position);

            if (tag == Utf8Tag)
            {
                int length = UInt16(bytes, ref position);
                Require(bytes, position, length);
                utf8[index] = Encoding.UTF8.GetString(bytes, position, length);
                position += length;
            }
            else
            {
                position += PayloadLength(tag);

                if (tag is 5 or 6)
                    index++;
            }
        }

        int accessFlags = UInt16(bytes, ref position);
        position += 4;
        int interfaces = UInt16(bytes, ref position);
        position += 2 * interfaces;
        SkipMembers(bytes, ref position);
        int methodCount = UInt16(bytes, ref position);
        List<ClassMethodSummary> methods = [];

        for (int method = 0; method < methodCount; method++)
        {
            int flags = UInt16(bytes, ref position);
            string name = Name(utf8, UInt16(bytes, ref position));
            string descriptor = Name(utf8, UInt16(bytes, ref position));
            SkipAttributes(bytes, ref position);
            methods.Add(new ClassMethodSummary(flags, name, descriptor));
        }

        return new ClassSummary(accessFlags, methods);
    }

    private static int Byte(byte[] bytes, ref int position)
    {
        Require(bytes, position, count: 1);

        return bytes[position++];
    }

    private static int Int32(byte[] bytes, ref int position)
    {
        Require(bytes, position, count: 4);
        int value = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(position));
        position += 4;

        return value;
    }

    private static string Name(string?[] utf8, int index)
    {
        return index > 0 && index < utf8.Length && utf8[index] is { } value ? value : throw new InvalidDataException($"Constant {index.ToString(System.Globalization.CultureInfo.InvariantCulture)} is not a Utf8 string.");
    }

    private static int PayloadLength(int tag)
    {
        return tag switch
        {
            3 or 4 or 9 or 10 or 11 or 12 or 17 or 18 => 4,
            5 or 6 => 8,
            7 or 8 or 16 or 19 or 20 => 2,
            15 => 3,
            _ => throw new InvalidDataException($"Unknown constant pool tag {tag.ToString(System.Globalization.CultureInfo.InvariantCulture)}.")
        };
    }

    private static void Require(byte[] bytes, int position, int count)
    {
        if (count < 0 || position < 0 || position + count > bytes.Length)
            throw new InvalidDataException(message: "The class file is truncated.");
    }

    private static void SkipAttributes(byte[] bytes, ref int position)
    {
        int attributes = UInt16(bytes, ref position);

        for (int attribute = 0; attribute < attributes; attribute++)
        {
            position += 2;
            int length = Int32(bytes, ref position);
            Require(bytes, position, length);
            position += length;
        }
    }

    private static void SkipMembers(byte[] bytes, ref int position)
    {
        int members = UInt16(bytes, ref position);

        for (int member = 0; member < members; member++)
        {
            position += 6;
            SkipAttributes(bytes, ref position);
        }
    }

    private static int UInt16(byte[] bytes, ref int position)
    {
        Require(bytes, position, count: 2);
        int value = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position));
        position += 2;

        return value;
    }
}

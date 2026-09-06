using System.Text;

namespace JvmBridge.Runtime;

/// <summary>Encodes JNI names and options as modified UTF-8 (UTF-16 code units).</summary>
public static class ModifiedUtf8
{
    public static byte[] Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        List<byte> bytes = new(value.Length * 3 + 1);
        foreach (char character in value)
        {
            if (character is > '\0' and <= '\x7f')
                bytes.Add((byte)character);
            else if (character <= '\x7ff')
            {
                bytes.Add((byte)(0xc0 | character >> 6));
                bytes.Add((byte)(0x80 | character & 0x3f));
            }
            else
            {
                bytes.Add((byte)(0xe0 | character >> 12));
                bytes.Add((byte)(0x80 | character >> 6 & 0x3f));
                bytes.Add((byte)(0x80 | character & 0x3f));
            }
        }
        bytes.Add(0);
        return bytes.ToArray();
    }

    public static string Decode(ReadOnlySpan<byte> value)
    {
        StringBuilder result = new();
        for (int index = 0; index < value.Length; index++)
        {
            byte first = value[index];
            if (first == 0)
                throw new FormatException("Modified UTF-8 must not contain embedded zero bytes.");
            if (first < 0x80)
                result.Append((char)first);
            else if ((first & 0xe0) == 0xc0 && index + 1 < value.Length)
            {
                byte second = value[++index];
                int character = ((first & 0x1f) << 6) | (second & 0x3f);
                if ((second & 0xc0) != 0x80 || character is > 0 and < 0x80)
                    throw new FormatException("Invalid modified UTF-8 sequence.");
                result.Append((char)character);
            }
            else if ((first & 0xf0) == 0xe0 && index + 2 < value.Length)
            {
                byte second = value[++index];
                byte third = value[++index];
                int character = ((first & 0x0f) << 12) | ((second & 0x3f) << 6) | (third & 0x3f);
                if ((second & 0xc0) != 0x80 || (third & 0xc0) != 0x80 || character < 0x800)
                    throw new FormatException("Invalid modified UTF-8 sequence.");
                result.Append((char)character);
            }
            else
                throw new FormatException("Invalid modified UTF-8 sequence.");
        }
        return result.ToString();
    }

    internal static unsafe string Decode(byte* value)
    {
        if (value == null)
            return string.Empty;
        int length = 0;
        while (value[length] != 0)
            length++;
        return Decode(new ReadOnlySpan<byte>(value, length));
    }
}

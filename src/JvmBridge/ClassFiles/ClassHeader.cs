using System.Diagnostics.CodeAnalysis;

namespace JvmBridge.ClassFiles;

/// <summary>
/// The part of a class file that decides whether a patch applies: its name, supertypes and class-level annotations. Reading walks the constant pool and skips fields, methods and code by length without decoding them,
/// so a class-file load hook can afford it for every class.
/// </summary>
internal sealed class ClassHeader
{
    private const string InvisibleAnnotations = "RuntimeInvisibleAnnotations";
    private const string VisibleAnnotations = "RuntimeVisibleAnnotations";

    private ClassHeader(string name, string? superName, string[] interfaces, string[] annotations)
    {
        Name = name;
        SuperName = superName;
        Interfaces = interfaces;
        Annotations = annotations;
    }

    /// <summary>Gets the internal names of the class-level annotation types, from the visible and invisible annotation attributes. Empty unless the header was read with annotations.</summary>
    internal IReadOnlyList<string> Annotations { get; }

    /// <summary>Gets the internal names of the directly implemented or extended interfaces.</summary>
    internal IReadOnlyList<string> Interfaces { get; }

    /// <summary>Gets the internal class name.</summary>
    internal string Name { get; }

    /// <summary>Gets the internal name of the direct superclass, or <see langword="null"/> for <c>java/lang/Object</c> and for module descriptors.</summary>
    internal string? SuperName { get; }

    /// <summary>Reads a header.</summary>
    /// <param name="bytes">The class-file bytes.</param>
    /// <param name="includeAnnotations">Whether to also read the class-level annotation types, which costs a walk over the members.</param>
    /// <returns>The header.</returns>
    /// <exception cref="ClassFormatException">The bytes are not a well-formed class file prefix.</exception>
    internal static ClassHeader Read(ReadOnlySpan<byte> bytes, bool includeAnnotations)
    {
        ClassReader reader = new(bytes);

        if (reader.ReadInt32() != unchecked((int)0xCAFEBABE))
            throw new ClassFormatException("The bytes are not a class file: the magic number is wrong.");

        reader.Skip(4);
        int count = reader.ReadUInt16();
        int[] utf8Start = new int[count];
        int[] utf8Length = new int[count];
        int[] classNames = new int[count];

        for (int index = 1; index < count; index++)
        {
            byte tag = reader.ReadByte();

            if (tag == (byte)ConstantTag.Utf8)
            {
                utf8Length[index] = reader.ReadUInt16();
                utf8Start[index] = reader.Position;
                reader.Skip(utf8Length[index]);
            }
            else if (tag == (byte)ConstantTag.Class)
                classNames[index] = reader.ReadUInt16();
            else
            {
                reader.Skip(PayloadLength(tag));

                if (tag is (byte)ConstantTag.Long or (byte)ConstantTag.Double)
                    index++;
            }
        }

        reader.Skip(2);
        string name = ClassName(bytes, utf8Start, utf8Length, classNames, reader.ReadUInt16());
        int superIndex = reader.ReadUInt16();
        string? superName = superIndex == 0 ? null : ClassName(bytes, utf8Start, utf8Length, classNames, superIndex);
        string[] interfaces = new string[reader.ReadUInt16()];

        for (int index = 0; index < interfaces.Length; index++)
            interfaces[index] = ClassName(bytes, utf8Start, utf8Length, classNames, reader.ReadUInt16());

        if (!includeAnnotations)
            return new ClassHeader(name, superName, interfaces, annotations: []);

        SkipMembers(ref reader);
        SkipMembers(ref reader);

        return new ClassHeader(name, superName, interfaces, ReadAnnotations(ref reader, bytes, utf8Start, utf8Length));
    }

    /// <summary>Reads a header without throwing.</summary>
    /// <param name="bytes">The class-file bytes.</param>
    /// <param name="includeAnnotations">Whether to also read the class-level annotation types.</param>
    /// <param name="header">The header when the bytes are well formed.</param>
    /// <returns><see langword="true"/> when the header was read.</returns>
    internal static bool TryRead(ReadOnlySpan<byte> bytes, bool includeAnnotations, [NotNullWhen(true)] out ClassHeader? header)
    {
        try
        {
            header = Read(bytes, includeAnnotations);

            return true;
        }
        catch (ClassFormatException)
        {
            header = null;

            return false;
        }
    }

    private static string ClassName(ReadOnlySpan<byte> bytes, int[] utf8Start, int[] utf8Length, int[] classNames, int classIndex)
    {
        if (classIndex <= 0 || classIndex >= classNames.Length || classNames[classIndex] == 0)
            throw new ClassFormatException($"Constant {classIndex} is not a class.");

        return Utf8(bytes, utf8Start, utf8Length, classNames[classIndex]);
    }

    private static int PayloadLength(byte tag)
    {
        return tag switch
        {
            (byte)ConstantTag.Integer or (byte)ConstantTag.Float or (byte)ConstantTag.Fieldref or (byte)ConstantTag.Methodref or (byte)ConstantTag.InterfaceMethodref => 4,
            (byte)ConstantTag.Long or (byte)ConstantTag.Double => 8,
            (byte)ConstantTag.String or (byte)ConstantTag.MethodType or (byte)ConstantTag.Module or (byte)ConstantTag.Package => 2,
            (byte)ConstantTag.NameAndType or (byte)ConstantTag.Dynamic or (byte)ConstantTag.InvokeDynamic => 4,
            (byte)ConstantTag.MethodHandle => 3,
            _ => throw new ClassFormatException($"Unknown constant pool tag {tag}.")
        };
    }

    private static string[] ReadAnnotations(ref ClassReader reader, ReadOnlySpan<byte> bytes, int[] utf8Start, int[] utf8Length)
    {
        List<string> annotations = [];
        int attributes = reader.ReadUInt16();

        for (int attribute = 0; attribute < attributes; attribute++)
        {
            string attributeName = Utf8(bytes, utf8Start, utf8Length, reader.ReadUInt16());
            int length = reader.ReadInt32();

            if (attributeName is not (VisibleAnnotations or InvisibleAnnotations))
            {
                reader.Skip(length);

                continue;
            }

            int end = reader.Position + length;
            int count = reader.ReadUInt16();

            for (int index = 0; index < count; index++)
            {
                string descriptor = Utf8(bytes, utf8Start, utf8Length, reader.ReadUInt16());

                if (descriptor.Length < 3 || descriptor[0] != 'L' || descriptor[^1] != ';')
                    throw new ClassFormatException($"Annotation type {descriptor} is not a class descriptor.");

                annotations.Add(descriptor[1..^1]);
                SkipPairs(ref reader);
            }

            if (reader.Position != end)
                throw new ClassFormatException($"An annotation attribute ends at {reader.Position} instead of {end}.");
        }

        return [.. annotations];
    }

    private static void SkipElementValue(ref ClassReader reader)
    {
        char tag = (char)reader.ReadByte();

        if (tag is 'B' or 'C' or 'D' or 'F' or 'I' or 'J' or 'S' or 'Z' or 's' or 'c')
            reader.Skip(2);
        else if (tag == 'e')
            reader.Skip(4);
        else if (tag == '@')
        {
            reader.Skip(2);
            SkipPairs(ref reader);
        }
        else if (tag == '[')
        {
            int values = reader.ReadUInt16();

            for (int index = 0; index < values; index++)
                SkipElementValue(ref reader);
        }
        else
            throw new ClassFormatException($"Unknown annotation element value tag '{tag}'.");
    }

    private static void SkipMembers(ref ClassReader reader)
    {
        int members = reader.ReadUInt16();

        for (int member = 0; member < members; member++)
        {
            reader.Skip(6);
            int attributes = reader.ReadUInt16();

            for (int attribute = 0; attribute < attributes; attribute++)
            {
                reader.Skip(2);
                reader.Skip(reader.ReadInt32());
            }
        }
    }

    private static void SkipPairs(ref ClassReader reader)
    {
        int pairs = reader.ReadUInt16();

        for (int pair = 0; pair < pairs; pair++)
        {
            reader.Skip(2);
            SkipElementValue(ref reader);
        }
    }

    private static string Utf8(ReadOnlySpan<byte> bytes, int[] utf8Start, int[] utf8Length, int index)
    {
        if (index <= 0 || index >= utf8Start.Length || utf8Start[index] == 0)
            throw new ClassFormatException($"Constant {index} is not a Utf8 string.");

        try
        {
            return Runtime.ModifiedUtf8.Decode(bytes.Slice(utf8Start[index], utf8Length[index]));
        }
        catch (FormatException exception)
        {
            throw new ClassFormatException($"The Utf8 constant {index} is not valid modified UTF-8.", exception);
        }
    }
}

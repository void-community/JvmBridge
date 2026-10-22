namespace JvmBridge.ClassFiles;

internal static class Opcodes
{
    internal const byte Nop = 0x00;
    internal const byte Ldc = 0x12;
    internal const byte LdcW = 0x13;
    internal const byte Ldc2W = 0x14;
    internal const byte Iinc = 0x84;
    internal const byte Ret = 0xa9;
    internal const byte TableSwitch = 0xaa;
    internal const byte LookupSwitch = 0xab;
    internal const byte GetStatic = 0xb2;
    internal const byte PutField = 0xb5;
    internal const byte InvokeVirtual = 0xb6;
    internal const byte InvokeSpecial = 0xb7;
    internal const byte InvokeStatic = 0xb8;
    internal const byte InvokeInterface = 0xb9;
    internal const byte InvokeDynamic = 0xba;
    internal const byte New = 0xbb;
    internal const byte ANewArray = 0xbd;
    internal const byte CheckCast = 0xc0;
    internal const byte InstanceOf = 0xc1;
    internal const byte Wide = 0xc4;
    internal const byte MultiANewArray = 0xc5;

    // Fixed instruction lengths by opcode; 0 marks switches, wide and reserved opcodes.
    private static readonly byte[] _lengths = BuildLengths();

    /// <summary>Gets the length of the instruction at an offset, validating that it fits in the code.</summary>
    internal static int GetLength(ReadOnlySpan<byte> code, int offset)
    {
        byte opcode = code[offset];
        int length;
        if (opcode == TableSwitch)
        {
            int table = offset + 1 + Padding(offset);
            RequireAvailable(code, table, 12, offset);
            int low = ReadInt(code, table + 4);
            int high = ReadInt(code, table + 8);
            long entries = (long)high - low + 1;
            if (high < low || entries > code.Length)
                throw new ClassFormatException($"Invalid tableswitch bounds at code offset {offset}: low {low}, high {high}.");
            length = 1 + Padding(offset) + 12 + (int)(entries * 4);
        }
        else if (opcode == LookupSwitch)
        {
            int table = offset + 1 + Padding(offset);
            RequireAvailable(code, table, 8, offset);
            int pairs = ReadInt(code, table + 4);
            if (pairs < 0 || pairs > code.Length)
                throw new ClassFormatException($"Invalid lookupswitch pair count at code offset {offset}.");
            length = 1 + Padding(offset) + 8 + (pairs * 8);
        }
        else if (opcode == Wide)
        {
            RequireAvailable(code, offset, 2, offset);
            byte modified = code[offset + 1];
            if (modified == Iinc)
                length = 6;
            else if (modified is (>= 0x15 and <= 0x19) or (>= 0x36 and <= 0x3a) or Ret)
                length = 4;
            else
                throw new ClassFormatException($"Opcode 0x{modified:x2} cannot follow wide at code offset {offset}.");
        }
        else
        {
            length = _lengths[opcode];
            if (length == 0)
                throw new ClassFormatException($"Unknown or reserved opcode 0x{opcode:x2} at code offset {offset}.");
        }
        if (offset + length > code.Length)
            throw new ClassFormatException($"Instruction at code offset {offset} runs past the end of the code.");
        return length;
    }

    /// <summary>Gets the constant pool index operand of an instruction, or <see langword="null"/> when it has none.</summary>
    internal static int? GetConstantIndex(ReadOnlySpan<byte> code, int offset)
    {
        byte opcode = code[offset];
        if (opcode == Ldc)
            return code[offset + 1];
        bool takesIndex = opcode is LdcW or Ldc2W or (>= GetStatic and <= New) or ANewArray or CheckCast or InstanceOf or MultiANewArray;
        return takesIndex ? (code[offset + 1] << 8) | code[offset + 2] : null;
    }

    /// <summary>Gets whether an instruction can reference a constant of this kind.</summary>
    internal static bool AcceptsConstant(byte opcode, ConstantTag tag)
    {
        return opcode switch
        {
            Ldc or LdcW => tag is ConstantTag.Integer or ConstantTag.Float or ConstantTag.String or ConstantTag.Class or ConstantTag.MethodType or ConstantTag.MethodHandle or ConstantTag.Dynamic,
            Ldc2W => tag is ConstantTag.Long or ConstantTag.Double or ConstantTag.Dynamic,
            >= GetStatic and <= PutField => tag is ConstantTag.Fieldref,
            InvokeVirtual => tag is ConstantTag.Methodref,
            InvokeSpecial or InvokeStatic => tag is ConstantTag.Methodref or ConstantTag.InterfaceMethodref,
            InvokeInterface => tag is ConstantTag.InterfaceMethodref,
            InvokeDynamic => tag is ConstantTag.InvokeDynamic,
            New or ANewArray or CheckCast or InstanceOf or MultiANewArray => tag is ConstantTag.Class,
            _ => false,
        };
    }

    /// <summary>Counts the operand slots of a method descriptor's parameters: two for long and double, one for everything else.</summary>
    internal static int CountArgumentSlots(string descriptor)
    {
        if (descriptor.Length == 0 || descriptor[0] != '(')
            throw new ClassFormatException($"'{descriptor}' is not a method descriptor.");

        int slots = 0;

        for (int index = 1; index < descriptor.Length && descriptor[index] != ')'; index++)
        {
            char type = descriptor[index];

            if (type is 'J' or 'D')
                slots += 2;
            else
            {
                slots++;

                while (index < descriptor.Length && descriptor[index] == '[')
                    index++;

                if (index < descriptor.Length && descriptor[index] == 'L')
                    index = descriptor.IndexOf(';', index);

                if (index < 0 || index >= descriptor.Length)
                    throw new ClassFormatException($"'{descriptor}' is not a method descriptor.");
            }
        }

        return slots;
    }

    private static int Padding(int offset) => (4 - ((offset + 1) % 4)) % 4;

    private static void RequireAvailable(ReadOnlySpan<byte> code, int start, int count, int instruction)
    {
        if (start + count > code.Length)
            throw new ClassFormatException($"Instruction at code offset {instruction} runs past the end of the code.");
    }

    private static int ReadInt(ReadOnlySpan<byte> code, int offset) => (code[offset] << 24) | (code[offset + 1] << 16) | (code[offset + 2] << 8) | code[offset + 3];

    private static byte[] BuildLengths()
    {
        byte[] lengths = new byte[256];
        void Fill(int first, int last, byte length)
        {
            for (int opcode = first; opcode <= last; opcode++)
                lengths[opcode] = length;
        }
        Fill(0x00, 0x0f, 1);
        lengths[0x10] = 2;
        lengths[0x11] = 3;
        lengths[0x12] = 2;
        lengths[0x13] = 3;
        lengths[0x14] = 3;
        Fill(0x15, 0x19, 2);
        Fill(0x1a, 0x35, 1);
        Fill(0x36, 0x3a, 2);
        Fill(0x3b, 0x83, 1);
        lengths[0x84] = 3;
        Fill(0x85, 0x98, 1);
        Fill(0x99, 0xa8, 3);
        lengths[0xa9] = 2;
        Fill(0xac, 0xb1, 1);
        Fill(0xb2, 0xb8, 3);
        lengths[0xb9] = 5;
        lengths[0xba] = 5;
        lengths[0xbb] = 3;
        lengths[0xbc] = 2;
        lengths[0xbd] = 3;
        lengths[0xbe] = 1;
        lengths[0xbf] = 1;
        lengths[0xc0] = 3;
        lengths[0xc1] = 3;
        lengths[0xc2] = 1;
        lengths[0xc3] = 1;
        lengths[0xc5] = 4;
        lengths[0xc6] = 3;
        lengths[0xc7] = 3;
        lengths[0xc8] = 5;
        lengths[0xc9] = 5;
        return lengths;
    }
}

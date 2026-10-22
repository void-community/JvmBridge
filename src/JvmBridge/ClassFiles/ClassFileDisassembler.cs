using System.Buffers.Binary;
using System.Globalization;

namespace JvmBridge.ClassFiles;

/// <summary>
/// Writes a class file as text in the style of <c>javap -c</c>: the class header, its fields and methods with their flags, and each method's instructions with their operands decoded. The text is deterministic, so a patch can be
/// checked by comparing the disassembly of its result with a stored snapshot.
/// </summary>
public static class ClassFileDisassembler
{
    private const string OpcodeNames =
        "nop aconst_null iconst_m1 iconst_0 iconst_1 iconst_2 iconst_3 iconst_4 iconst_5 lconst_0 lconst_1 fconst_0 fconst_1 fconst_2 dconst_0 dconst_1 bipush sipush ldc ldc_w ldc2_w "
        + "iload lload fload dload aload iload_0 iload_1 iload_2 iload_3 lload_0 lload_1 lload_2 lload_3 fload_0 fload_1 fload_2 fload_3 dload_0 dload_1 dload_2 dload_3 "
        + "aload_0 aload_1 aload_2 aload_3 iaload laload faload daload aaload baload caload saload istore lstore fstore dstore astore "
        + "istore_0 istore_1 istore_2 istore_3 lstore_0 lstore_1 lstore_2 lstore_3 fstore_0 fstore_1 fstore_2 fstore_3 dstore_0 dstore_1 dstore_2 dstore_3 "
        + "astore_0 astore_1 astore_2 astore_3 iastore lastore fastore dastore aastore bastore castore sastore pop pop2 dup dup_x1 dup_x2 dup2 dup2_x1 dup2_x2 swap "
        + "iadd ladd fadd dadd isub lsub fsub dsub imul lmul fmul dmul idiv ldiv fdiv ddiv irem lrem frem drem ineg lneg fneg dneg ishl lshl ishr lshr iushr lushr iand land ior lor ixor lxor "
        + "iinc i2l i2f i2d l2i l2f l2d f2i f2l f2d d2i d2l d2f i2b i2c i2s lcmp fcmpl fcmpg dcmpl dcmpg ifeq ifne iflt ifge ifgt ifle "
        + "if_icmpeq if_icmpne if_icmplt if_icmpge if_icmpgt if_icmple if_acmpeq if_acmpne goto jsr ret tableswitch lookupswitch ireturn lreturn freturn dreturn areturn return "
        + "getstatic putstatic getfield putfield invokevirtual invokespecial invokestatic invokeinterface invokedynamic new newarray anewarray arraylength athrow checkcast instanceof "
        + "monitorenter monitorexit wide multianewarray ifnull ifnonnull goto_w jsr_w";

    private static readonly string[] Names = OpcodeNames.Split(' ');

    /// <summary>Disassembles a class.</summary>
    /// <param name="classBytes">The class-file bytes.</param>
    /// <param name="method">A method to show, as its name (every overload) or <c>name(descriptor)</c>, or <see langword="null"/> for the whole class.</param>
    /// <returns>The text, with one line per field, method and instruction.</returns>
    /// <exception cref="ClassFormatException">The bytes are not a well-formed class file.</exception>
    public static string Disassemble(ReadOnlySpan<byte> classBytes, string? method = null)
    {
        JavaClassFile file = JavaClassFile.Read(classBytes);
        List<string> lines = [];

        if (method is null)
        {
            string superName = file.SuperName ?? "none";
            string interfaces = file.Interfaces.Count == 0 ? string.Empty : " implements " + string.Join(separator: ", ", file.Interfaces);
            lines.Add($"class {file.Name} extends {superName}{interfaces}");
            lines.Add($"  version {file.MajorVersion.ToString(CultureInfo.InvariantCulture)}.{file.MinorVersion.ToString(CultureInfo.InvariantCulture)}, flags {Flags(file.AccessFlags, kind: "class")}");

            foreach (ClassMember field in file.Fields)
                lines.Add($"  field {Flags(field.AccessFlags, kind: "field")} {field.Descriptor} {field.Name}");
        }

        foreach (ClassMember member in file.Methods.Where(candidate => Selected(candidate, method)))
        {
            lines.Add($"  method {Flags(member.AccessFlags, kind: "method")} {member.Name}{member.Descriptor}");

            if (member.Code is not { } code)
                continue;

            lines.Add($"    max_stack={code.MaxStack.ToString(CultureInfo.InvariantCulture)} max_locals={code.MaxLocals.ToString(CultureInfo.InvariantCulture)}");
            ReadOnlySpan<byte> bytes = classBytes.Slice(code.CodeOffset, code.CodeLength);

            foreach (Instruction instruction in code.Instructions)
                lines.Add($"      {instruction.Offset.ToString(CultureInfo.InvariantCulture)}: {Describe(file, bytes, instruction)}");
        }

        return string.Join(separator: "\n", lines) + "\n";
    }

    private static string Constant(JavaClassFile file, int index)
    {
        ConstantPoolEntry? entry = index > 0 && index < file.ConstantPool.Count ? file.ConstantPool[index] : null;

        if (entry is IntegerEntry integer)
            return "int " + integer.Value.ToString(CultureInfo.InvariantCulture);

        if (entry is FloatEntry real)
            return "float " + real.Value.ToString("R", CultureInfo.InvariantCulture);

        if (entry is LongEntry wide)
            return "long " + wide.Value.ToString(CultureInfo.InvariantCulture) + "L";

        if (entry is DoubleEntry twice)
            return "double " + twice.Value.ToString("R", CultureInfo.InvariantCulture);

        if (entry is StringEntry text)
            return "String " + Quote(file.GetUtf8(text.StringIndex));

        if (entry is ClassEntry type)
            return "class " + file.GetUtf8(type.NameIndex);

        if (entry is MemberReferenceEntry)
        {
            MemberReference reference = file.GetMemberReference(index);
            string kind = entry.Tag == ConstantTag.Fieldref ? "Field" : entry.Tag == ConstantTag.InterfaceMethodref ? "InterfaceMethod" : "Method";

            return $"{kind} {reference.Owner}.{reference.Name}:{reference.Descriptor}";
        }

        if (entry is DynamicEntry dynamic && file.ConstantPool[dynamic.NameAndTypeIndex] is NameAndTypeEntry signature)
            return $"InvokeDynamic #{dynamic.BootstrapMethodIndex.ToString(CultureInfo.InvariantCulture)}:{file.GetUtf8(signature.NameIndex)}:{file.GetUtf8(signature.DescriptorIndex)}";

        return "constant #" + index.ToString(CultureInfo.InvariantCulture);
    }

    private static string Describe(JavaClassFile file, ReadOnlySpan<byte> code, Instruction instruction)
    {
        int opcode = instruction.Opcode;
        string name = opcode < Names.Length ? Names[opcode] : "opcode_" + opcode.ToString(CultureInfo.InvariantCulture);
        int at = instruction.Offset;

        if (opcode is 0x10)
            return name + " " + ((sbyte)code[at + 1]).ToString(CultureInfo.InvariantCulture);

        if (opcode is 0x11)
            return name + " " + BinaryPrimitives.ReadInt16BigEndian(code[(at + 1)..]).ToString(CultureInfo.InvariantCulture);

        if (opcode is 0x12)
            return $"{name} #{code[at + 1].ToString(CultureInfo.InvariantCulture)} // {Constant(file, code[at + 1])}";

        if (instruction.ConstantIndex is { } index)
            return $"{name} #{index.ToString(CultureInfo.InvariantCulture)} // {Constant(file, index)}" + (opcode == 0xc5 ? " dimensions " + code[at + 3].ToString(CultureInfo.InvariantCulture) : string.Empty);

        if (opcode is (>= 0x15 and <= 0x19) or (>= 0x36 and <= 0x3a) or 0xa9)
            return name + " " + code[at + 1].ToString(CultureInfo.InvariantCulture);

        if (opcode is 0x84)
            return $"{name} {code[at + 1].ToString(CultureInfo.InvariantCulture)}, {((sbyte)code[at + 2]).ToString(CultureInfo.InvariantCulture)}";

        if (opcode is (>= 0x99 and <= 0xa8) or 0xc6 or 0xc7)
            return name + " " + (at + BinaryPrimitives.ReadInt16BigEndian(code[(at + 1)..])).ToString(CultureInfo.InvariantCulture);

        if (opcode is 0xc8 or 0xc9)
            return name + " " + (at + BinaryPrimitives.ReadInt32BigEndian(code[(at + 1)..])).ToString(CultureInfo.InvariantCulture);

        if (opcode is 0xbc)
            return name + " " + ArrayType(code[at + 1]);

        if (opcode is 0xaa or 0xab)
            return name + " " + Switch(code, at, table: opcode == 0xaa);

        if (opcode is 0xc4)
            return Wide(code, at);

        return name;
    }

    private static string ArrayType(int code)
    {
        string[] names = ["boolean", "char", "float", "double", "byte", "short", "int", "long"];

        return code is >= 4 and <= 11 ? names[code - 4] : "type_" + code.ToString(CultureInfo.InvariantCulture);
    }

    private static string Flags(int flags, string kind)
    {
        List<string> names = [];
        (int Bit, string Name)[] shared = [(0x0001, "public"), (0x0002, "private"), (0x0004, "protected"), (0x0008, "static"), (0x0010, "final")];

        foreach ((int bit, string text) in shared)
        {
            bool applies = kind != "class" || bit is 0x0001 or 0x0010;

            if ((flags & bit) != 0 && applies)
                names.Add(text);
        }

        if (kind == "method")
            AddFlags(flags, names, [(0x0020, "synchronized"), (0x0040, "bridge"), (0x0080, "varargs"), (0x0100, "native"), (0x0400, "abstract"), (0x0800, "strict"), (0x1000, "synthetic")]);
        else if (kind == "field")
            AddFlags(flags, names, [(0x0040, "volatile"), (0x0080, "transient"), (0x1000, "synthetic"), (0x4000, "enum")]);
        else
            AddFlags(flags, names, [(0x0200, "interface"), (0x0400, "abstract"), (0x1000, "synthetic"), (0x2000, "annotation"), (0x4000, "enum"), (0x8000, "module")]);

        return names.Count == 0 ? "package" : string.Join(separator: " ", names);
    }

    private static void AddFlags(int flags, List<string> names, (int Bit, string Name)[] known)
    {
        foreach ((int bit, string text) in known)
        {
            if ((flags & bit) != 0)
                names.Add(text);
        }
    }

    private static string Quote(string value)
    {
        string shown = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);

        return "\"" + shown + "\"";
    }

    private static bool Selected(ClassMember member, string? method)
    {
        return method is null || member.Name == method || member.Name + member.Descriptor == method;
    }

    private static string Switch(ReadOnlySpan<byte> code, int at, bool table)
    {
        int position = at + 1 + ((4 - ((at + 1) % 4)) % 4);
        int defaultTarget = at + BinaryPrimitives.ReadInt32BigEndian(code[position..]);
        position += 4;
        List<string> cases = [];

        if (table)
        {
            int low = BinaryPrimitives.ReadInt32BigEndian(code[position..]);
            int high = BinaryPrimitives.ReadInt32BigEndian(code[(position + 4)..]);
            position += 8;

            for (int value = low; value <= high; value++, position += 4)
                cases.Add($"{value.ToString(CultureInfo.InvariantCulture)}: {(at + BinaryPrimitives.ReadInt32BigEndian(code[position..])).ToString(CultureInfo.InvariantCulture)}");
        }
        else
        {
            int count = BinaryPrimitives.ReadInt32BigEndian(code[position..]);
            position += 4;

            for (int index = 0; index < count; index++, position += 8)
                cases.Add($"{BinaryPrimitives.ReadInt32BigEndian(code[position..]).ToString(CultureInfo.InvariantCulture)}: {(at + BinaryPrimitives.ReadInt32BigEndian(code[(position + 4)..])).ToString(CultureInfo.InvariantCulture)}");
        }

        cases.Add("default: " + defaultTarget.ToString(CultureInfo.InvariantCulture));

        return "{ " + string.Join(separator: ", ", cases) + " }";
    }

    private static string Wide(ReadOnlySpan<byte> code, int at)
    {
        int inner = code[at + 1];
        string name = inner < Names.Length ? Names[inner] : "opcode_" + inner.ToString(CultureInfo.InvariantCulture);
        int index = BinaryPrimitives.ReadUInt16BigEndian(code[(at + 2)..]);

        return inner == 0x84
            ? $"wide {name} {index.ToString(CultureInfo.InvariantCulture)}, {BinaryPrimitives.ReadInt16BigEndian(code[(at + 4)..]).ToString(CultureInfo.InvariantCulture)}"
            : $"wide {name} {index.ToString(CultureInfo.InvariantCulture)}";
    }
}

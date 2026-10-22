using System.Buffers.Binary;

using JvmBridge.ClassFiles.Advice;

namespace JvmBridge.ClassFiles;

/// <summary>
/// Edits a class file in ways that keep every existing constant pool index and code offset: new constants are appended after the existing ones, and everything else is
/// rewritten in place at the same length. Code, exception tables, stack map frames and other attributes therefore stay valid without being recomputed. The one exception is
/// the method advice that <c>ClassPatch</c> adds: it moves a method's code, byte for byte, into a new method, and gives the old method a short straight-line body.
/// </summary>
public sealed class ClassFileEditor
{
    private byte[] _bytes;
    private JavaClassFile? _model;

    /// <summary>Initializes an editor over a copy of a class file.</summary>
    /// <param name="bytes">The class file bytes.</param>
    /// <exception cref="ClassFormatException">The input is not a well-formed class file.</exception>
    public ClassFileEditor(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes.ToArray();
        _model = JavaClassFile.Read(_bytes);
    }

    /// <summary>Gets a read-only view of the class as edited so far. Offsets and indices in it describe that state only.</summary>
    public JavaClassFile Model => _model ??= JavaClassFile.Read(_bytes);

    /// <summary>Adds a Utf8 constant once.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Find<Utf8Entry>(entry => entry.Value == value) ?? Append(ConstantEncoding.Utf8(value), slots: 1);
    }

    /// <summary>Adds a Class constant once.</summary>
    /// <param name="name">The internal class name.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddClass(string name)
    {
        return Atomically(() =>
        {
            int utf8 = AddUtf8(name);
            return Find<ClassEntry>(entry => entry.NameIndex == utf8) ?? Append(ConstantEncoding.Reference(ConstantTag.Class, utf8), slots: 1);
        });
    }

    /// <summary>Adds a String constant once.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddString(string value)
    {
        return Atomically(() =>
        {
            int utf8 = AddUtf8(value);
            return Find<StringEntry>(entry => entry.StringIndex == utf8) ?? Append(ConstantEncoding.Reference(ConstantTag.String, utf8), slots: 1);
        });
    }

    /// <summary>Adds a NameAndType constant once.</summary>
    /// <param name="name">The member name.</param>
    /// <param name="descriptor">The member descriptor.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddNameAndType(string name, string descriptor)
    {
        return Atomically(() =>
        {
            int nameIndex = AddUtf8(name);
            int descriptorIndex = AddUtf8(descriptor);
            return Find<NameAndTypeEntry>(entry => entry.NameIndex == nameIndex && entry.DescriptorIndex == descriptorIndex) ?? Append(ConstantEncoding.Reference(ConstantTag.NameAndType, nameIndex, descriptorIndex), slots: 1);
        });
    }

    /// <summary>Adds a field reference once.</summary>
    /// <param name="owner">The internal name of the owner.</param>
    /// <param name="name">The field name.</param>
    /// <param name="descriptor">The field descriptor.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddFieldReference(string owner, string name, string descriptor) => AddMemberReference(ConstantTag.Fieldref, owner, name, descriptor);

    /// <summary>Adds a method reference of a class once.</summary>
    /// <param name="owner">The internal name of the owner.</param>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddMethodReference(string owner, string name, string descriptor) => AddMemberReference(ConstantTag.Methodref, owner, name, descriptor);

    /// <summary>Adds a method reference of an interface once.</summary>
    /// <param name="owner">The internal name of the interface.</param>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddInterfaceMethodReference(string owner, string name, string descriptor) => AddMemberReference(ConstantTag.InterfaceMethodref, owner, name, descriptor);

    /// <summary>Adds an Integer constant once.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddInteger(int value) => Find<IntegerEntry>(entry => entry.Value == value) ?? Append(ConstantEncoding.Integer(value), slots: 1);

    /// <summary>Adds a Float constant once.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddFloat(float value) => Find<FloatEntry>(entry => BitConverter.SingleToInt32Bits(entry.Value) == BitConverter.SingleToInt32Bits(value)) ?? Append(ConstantEncoding.Float(value), slots: 1);

    /// <summary>Adds a Long constant once. It occupies two slots.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddLong(long value) => Find<LongEntry>(entry => entry.Value == value) ?? Append(ConstantEncoding.Long(value), slots: 2);

    /// <summary>Adds a Double constant once. It occupies two slots.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddDouble(double value) => Find<DoubleEntry>(entry => BitConverter.DoubleToInt64Bits(entry.Value) == BitConverter.DoubleToInt64Bits(value)) ?? Append(ConstantEncoding.Double(value), slots: 2);

    /// <summary>Points every matching field or method reference constant at another member, so all instructions that use it are retargeted.</summary>
    /// <param name="from">The reference to find; owner, name, descriptor and kind must all match.</param>
    /// <param name="toOwner">The internal name of the new owner.</param>
    /// <param name="toName">The new member name, or <see langword="null"/> to keep the name.</param>
    /// <param name="toDescriptor">The new descriptor, or <see langword="null"/> to keep the descriptor.</param>
    /// <returns>The number of reference constants retargeted.</returns>
    public int RetargetMemberReference(MemberReference from, string toOwner, string? toName = null, string? toDescriptor = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(toOwner);
        return Atomically(() =>
        {
            int[] matches = [.. Model.ConstantPool.OfType<MemberReferenceEntry>().Where(entry => Model.GetMemberReference(entry.Index) == from).Select(entry => entry.Index)];
            if (matches.Length == 0)
                return 0;
            int owner = AddClass(toOwner);
            int nameAndType = AddNameAndType(toName ?? from.Name, toDescriptor ?? from.Descriptor);
            foreach (int index in matches)
            {
                MemberReferenceEntry entry = Model.GetEntry<MemberReferenceEntry>(index);
                WriteUInt16(entry.Offset + 1, owner);
                WriteUInt16(entry.Offset + 3, nameAndType);
            }
            return matches.Length;
        });
    }

    /// <summary>
    /// Replaces matching invocations in place. The call becomes <c>invokestatic</c> of another owner at the same length. For <c>invokevirtual</c> and <c>invokeinterface</c> the
    /// receiver becomes the first parameter of the replacement, and the padding of <c>invokeinterface</c> becomes <c>nop</c>. Other call sites of the same method are left alone.
    /// </summary>
    /// <param name="from">The invocation to find; kind, owner, name and descriptor must all match.</param>
    /// <param name="toOwner">The internal name of the class that declares the replacement.</param>
    /// <param name="toName">The replacement name, or <see langword="null"/> to keep the name.</param>
    /// <param name="toDescriptor">The replacement descriptor, or <see langword="null"/> to derive it: unchanged for static calls, with the receiver type prepended otherwise.</param>
    /// <param name="methodName">Limits the search to methods with this name, or <see langword="null"/> for every method.</param>
    /// <param name="methodDescriptor">Limits the search to methods with this descriptor, or <see langword="null"/> for any.</param>
    /// <param name="ownerIsInterface">Whether <paramref name="toOwner"/> is an interface, whose static methods need an interface method reference (class files of version 52 or later).</param>
    /// <returns>The number of instructions replaced.</returns>
    public int ReplaceInvocation(JavaMethod from, string toOwner, string? toName = null, string? toDescriptor = null, string? methodName = null, string? methodDescriptor = null, bool ownerIsInterface = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(toOwner);
        return Atomically(() =>
        {
            if (FindInvocations(from, methodName, methodDescriptor).Count == 0)
                return 0;
            string descriptor = toDescriptor ?? (from.Kind == JavaMethodKind.Static ? from.Descriptor : WithReceiver(from.Owner, from.Descriptor));
            string name = toName ?? from.Name;
            int target = ownerIsInterface ? AddInterfaceMethodReference(toOwner, name, descriptor) : AddMethodReference(toOwner, name, descriptor);
            List<int> sites = FindInvocations(from, methodName, methodDescriptor);
            foreach (int site in sites)
            {
                byte original = _bytes[site];
                _bytes[site] = Opcodes.InvokeStatic;
                WriteUInt16(site + 1, target);
                if (original == Opcodes.InvokeInterface)
                {
                    _bytes[site + 3] = Opcodes.Nop;
                    _bytes[site + 4] = Opcodes.Nop;
                }
            }
            _model = null;
            return sites.Count;
        });
    }

    /// <summary>Points one instruction at another constant pool entry. The entry must be of a kind the instruction accepts, and the argument count of an <c>invokeinterface</c> is updated to match.</summary>
    /// <param name="methodName">The name of the method that contains the instruction.</param>
    /// <param name="methodDescriptor">The method descriptor, or <see langword="null"/> when the name is not overloaded.</param>
    /// <param name="instructionOffset">The offset of the instruction from the start of the method's code.</param>
    /// <param name="constantIndex">The new constant pool index; it must already exist.</param>
    public void RewriteConstantOperand(string methodName, string? methodDescriptor, int instructionOffset, int constantIndex)
    {
        ClassMember method = RequireMethod(methodName, methodDescriptor);
        CodeAttribute code = method.Code ?? throw new ClassFormatException($"{method.Name}{method.Descriptor} has no code.");
        Instruction instruction = code.Instructions.FirstOrDefault(candidate => candidate.Offset == instructionOffset);
        if (instruction.Length == 0 || instruction.ConstantIndex is null)
            throw new ClassFormatException($"There is no instruction with a constant operand at offset {instructionOffset} of {method.Name}{method.Descriptor}.");
        ConstantPoolEntry entry = Model.ConstantPool.ElementAtOrDefault(constantIndex) ?? throw new ArgumentOutOfRangeException(nameof(constantIndex), constantIndex, "The constant pool has no usable entry at this index.");
        if (!Opcodes.AcceptsConstant(instruction.Opcode, entry.Tag))
            throw new ArgumentException($"Instruction 0x{instruction.Opcode:x2} cannot reference a {entry.Tag} constant.", nameof(constantIndex));
        int at = code.CodeOffset + instruction.Offset;
        if (instruction.Opcode == Opcodes.Ldc)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(constantIndex, byte.MaxValue);
            _bytes[at + 1] = (byte)constantIndex;
        }
        else
            WriteUInt16(at + 1, constantIndex);
        if (instruction.Opcode == Opcodes.InvokeInterface)
            _bytes[at + 3] = checked((byte)(1 + Opcodes.CountArgumentSlots(Model.GetMemberReference(constantIndex).Descriptor)));
        _model = null;
    }

    /// <summary>Rewrites bytes of a method's code at the same length when they match what the caller expects. The result must still decode as instructions, or nothing changes and an exception is thrown; keeping it verifiable is the caller's responsibility.</summary>
    /// <param name="methodName">The method name.</param>
    /// <param name="methodDescriptor">The method descriptor, or <see langword="null"/> when the name is not overloaded.</param>
    /// <param name="codeOffset">The offset from the start of the method's code.</param>
    /// <param name="expected">The bytes that must be there.</param>
    /// <param name="replacement">The bytes to write; the same length as <paramref name="expected"/>.</param>
    /// <returns><see langword="true"/> when the expected bytes were found and replaced.</returns>
    public bool ReplaceCode(string methodName, string? methodDescriptor, int codeOffset, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> replacement)
    {
        if (expected.Length != replacement.Length)
            throw new ArgumentException("The replacement must have the same length as the expected bytes, so code offsets stay valid.", nameof(replacement));
        ClassMember method = RequireMethod(methodName, methodDescriptor);
        CodeAttribute code = method.Code ?? throw new ClassFormatException($"{method.Name}{method.Descriptor} has no code.");
        if (codeOffset < 0 || codeOffset > code.CodeLength - expected.Length)
            return false;
        int at = code.CodeOffset + codeOffset;
        if (!_bytes.AsSpan(at, expected.Length).SequenceEqual(expected))
            return false;
        byte[] candidate = (byte[])_bytes.Clone();
        replacement.CopyTo(candidate.AsSpan(at, expected.Length));
        try { _ = JavaClassFile.Read(candidate); }
        catch (ClassFormatException exception) { throw new ClassFormatException($"The replacement does not leave valid code in {method.Name}{method.Descriptor}: {exception.Message}", exception); }
        _bytes = candidate;
        _model = null;
        return true;
    }

    /// <summary>Makes String constants with one value point at another value. Every use of the constant changes.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>The number of String constants changed.</returns>
    public int ReplaceString(string oldValue, string newValue)
    {
        ArgumentNullException.ThrowIfNull(oldValue);
        ArgumentNullException.ThrowIfNull(newValue);
        int[] matches = [.. Model.ConstantPool.OfType<StringEntry>().Where(entry => Model.GetUtf8(entry.StringIndex) == oldValue).Select(entry => entry.Index)];
        if (matches.Length == 0)
            return 0;
        int utf8 = AddUtf8(newValue);
        foreach (int index in matches)
            WriteUInt16(Model.GetEntry<StringEntry>(index).Offset + 1, utf8);
        return matches.Length;
    }

    /// <summary>Changes an Integer constant. Small values that the compiler encodes as <c>bipush</c> or <c>sipush</c> are not constants and are not affected.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>The number of constants changed.</returns>
    public int ReplaceInteger(int oldValue, int newValue) => Rewrite<IntegerEntry>(entry => entry.Value == oldValue, offset => BinaryPrimitives.WriteInt32BigEndian(_bytes.AsSpan(offset), newValue));

    /// <summary>Changes a Float constant.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>The number of constants changed.</returns>
    public int ReplaceFloat(float oldValue, float newValue) => Rewrite<FloatEntry>(entry => BitConverter.SingleToInt32Bits(entry.Value) == BitConverter.SingleToInt32Bits(oldValue), offset => BinaryPrimitives.WriteInt32BigEndian(_bytes.AsSpan(offset), BitConverter.SingleToInt32Bits(newValue)));

    /// <summary>Changes a Long constant.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>The number of constants changed.</returns>
    public int ReplaceLong(long oldValue, long newValue) => Rewrite<LongEntry>(entry => entry.Value == oldValue, offset => BinaryPrimitives.WriteInt64BigEndian(_bytes.AsSpan(offset), newValue));

    /// <summary>Changes a Double constant.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>The number of constants changed.</returns>
    public int ReplaceDouble(double oldValue, double newValue) => Rewrite<DoubleEntry>(entry => BitConverter.DoubleToInt64Bits(entry.Value) == BitConverter.DoubleToInt64Bits(oldValue), offset => BinaryPrimitives.WriteInt64BigEndian(_bytes.AsSpan(offset), BitConverter.DoubleToInt64Bits(newValue)));

    /// <summary>Replaces the class access flags.</summary>
    /// <param name="accessFlags">The new flags.</param>
    public void SetAccess(int accessFlags)
    {
        WriteUInt16(Model.AccessFlagsOffset, accessFlags);
        _model = null;
    }

    /// <summary>Replaces the access flags of matching fields.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="descriptor">The field descriptor, or <see langword="null"/> to match any.</param>
    /// <param name="accessFlags">The new flags.</param>
    /// <returns>The number of fields changed.</returns>
    public int SetFieldAccess(string name, string? descriptor, int accessFlags) => SetMemberAccess(Model.Fields, name, descriptor, accessFlags);

    /// <summary>Replaces the access flags of matching methods.</summary>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor, or <see langword="null"/> to match every overload.</param>
    /// <param name="accessFlags">The new flags.</param>
    /// <returns>The number of methods changed.</returns>
    public int SetMethodAccess(string name, string? descriptor, int accessFlags) => SetMemberAccess(Model.Methods, name, descriptor, accessFlags);

    /// <summary>
    /// Moves the code of a method into a new private synthetic method and gives the method the code of a stub that calls it. The method keeps its flags, signature and annotations; the new method is <c>static</c> when it is. This is the one edit that adds a method, so it
    /// is only valid for a class that has not been defined yet: a loaded class cannot gain methods.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor.</param>
    /// <param name="bodyName">The name of the new method, which holds a verbatim copy of the old <c>Code</c> attribute.</param>
    /// <param name="stub">The code of the method from now on.</param>
    internal void ReplaceBodyWithStub(string name, string descriptor, string bodyName, StubCode stub)
    {
        Atomically(() =>
        {
            int bodyNameIndex = AddUtf8(bodyName);
            int codeNameIndex = AddUtf8("Code");
            ClassMember method = RequireMethod(name, descriptor);

            if (method.Code is null)
                throw new ClassFormatException($"{name}{descriptor} has no code.");

            int flags = (method.AccessFlags & 0x0008) | 0x0002 | 0x1000;
            int start = method.AccessFlagsOffset;
            ReadOnlySpan<byte> original = _bytes;

            using MemoryStream body = new();

            WriteUInt16To(body, flags);
            WriteUInt16To(body, bodyNameIndex);
            body.Write(original.Slice(start + 4, 2));
            WriteUInt16To(body, value: 1);
            body.Write(original[method.CodeAttributeStart..method.CodeAttributeEnd]);

            using MemoryStream result = new();

            int count = BinaryPrimitives.ReadUInt16BigEndian(original[Model.MethodsCountOffset..]);

            result.Write(original[..Model.MethodsCountOffset]);
            WriteUInt16To(result, count + 1);
            result.Write(original[(Model.MethodsCountOffset + 2)..method.CodeAttributeStart]);
            result.Write(AdviceStub.CodeAttribute(codeNameIndex, stub));
            result.Write(original[method.CodeAttributeEnd..Model.MethodsEnd]);
            result.Write(body.ToArray());
            result.Write(original[Model.MethodsEnd..]);

            byte[] candidate = result.ToArray();

            _ = JavaClassFile.Read(candidate);
            _bytes = candidate;
            _model = null;
        });
    }

    /// <summary>Produces the edited class file.</summary>
    /// <returns>A copy of the class file bytes.</returns>
    public byte[] ToArray() => (byte[])_bytes.Clone();

    private static string WithReceiver(string owner, string descriptor)
    {
        if (descriptor.Length == 0 || descriptor[0] != '(')
            throw new ArgumentException($"'{descriptor}' is not a method descriptor.", nameof(descriptor));
        string receiver = owner.StartsWith('[') ? owner : $"L{owner};";
        return string.Concat("(", receiver, descriptor.AsSpan(1));
    }

    private int AddMemberReference(ConstantTag kind, string owner, string name, string descriptor)
    {
        return Atomically(() =>
        {
            int ownerIndex = AddClass(owner);
            int nameAndType = AddNameAndType(name, descriptor);
            return Find<MemberReferenceEntry>(entry => entry.Kind == kind && entry.ClassIndex == ownerIndex && entry.NameAndTypeIndex == nameAndType) ?? Append(ConstantEncoding.Reference(kind, ownerIndex, nameAndType), slots: 1);
        });
    }

    private static void WriteUInt16To(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private void Atomically(Action edit)
    {
        byte[] snapshot = (byte[])_bytes.Clone();

        try { edit(); }
        catch (Exception)
        {
            _bytes = snapshot;
            _model = null;

            throw;
        }
    }

    /// <summary>Runs a multi-step edit so that a failure part-way leaves the class exactly as it was.</summary>
    private int Atomically(Func<int> edit)
    {
        byte[] snapshot = (byte[])_bytes.Clone();

        try { return edit(); }
        catch (Exception)
        {
            _bytes = snapshot;
            _model = null;

            throw;
        }
    }

    private int? Find<T>(Func<T, bool> match)
        where T : ConstantPoolEntry => Model.ConstantPool.OfType<T>().FirstOrDefault(match)?.Index;

    private int Append(byte[] entry, int slots)
    {
        int index = Model.ConstantPool.Count;
        if (index + slots > ushort.MaxValue)
            throw new InvalidOperationException("The constant pool is full (65535 slots).");
        int end = Model.ConstantPoolEnd;
        byte[] result = new byte[_bytes.Length + entry.Length];
        _bytes.AsSpan(0, end).CopyTo(result);
        entry.CopyTo(result, end);
        _bytes.AsSpan(end).CopyTo(result.AsSpan(end + entry.Length));
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(8), (ushort)(index + slots));
        _bytes = result;
        _model = null;
        return index;
    }

    private int Rewrite<T>(Func<T, bool> match, Action<int> write)
        where T : ConstantPoolEntry
    {
        int[] offsets = [.. Model.ConstantPool.OfType<T>().Where(match).Select(entry => entry.Offset + 1)];
        foreach (int offset in offsets)
            write(offset);
        if (offsets.Length > 0)
            _model = null;
        return offsets.Length;
    }

    private int SetMemberAccess(IReadOnlyList<ClassMember> members, string name, string? descriptor, int accessFlags)
    {
        int[] offsets = [.. members.Where(member => member.Name == name && (descriptor is null || member.Descriptor == descriptor)).Select(member => member.AccessFlagsOffset)];
        foreach (int offset in offsets)
            WriteUInt16(offset, accessFlags);
        return offsets.Length;
    }

    private ClassMember RequireMethod(string name, string? descriptor)
    {
        ClassMember[] candidates = [.. Model.Methods.Where(method => method.Name == name && (descriptor is null || method.Descriptor == descriptor))];
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new ClassFormatException($"The class has no method {name}{descriptor}."),
            _ => throw new ClassFormatException($"{name} is overloaded; specify its descriptor."),
        };
    }

    private List<int> FindInvocations(JavaMethod from, string? methodName, string? methodDescriptor)
    {
        List<int> sites = [];
        foreach (ClassMember method in Model.Methods)
        {
            if (method.Code is null || (methodName is not null && method.Name != methodName) || (methodDescriptor is not null && method.Descriptor != methodDescriptor))
                continue;
            foreach (Instruction instruction in method.Code.Instructions)
            {
                if (instruction.Opcode == from.Opcode && instruction.ConstantIndex is int index && Matches(Model.GetMemberReference(index), from))
                    sites.Add(method.Code.CodeOffset + instruction.Offset);
            }
        }
        return sites;
    }

    private static bool Matches(MemberReference reference, JavaMethod method) => reference.Kind != ConstantTag.Fieldref && reference.Owner == method.Owner && reference.Name == method.Name && reference.Descriptor == method.Descriptor;

    private void WriteUInt16(int offset, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ushort.MaxValue);
        BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan(offset), (ushort)value);
        _model = null;
    }
}

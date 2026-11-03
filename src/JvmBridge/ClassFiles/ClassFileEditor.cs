using System.Buffers.Binary;

using JvmBridge.ClassFiles.Advice;
using JvmBridge.ClassFiles.Code;

namespace JvmBridge.ClassFiles;

/// <summary>
/// Edits a class file in ways that keep every existing constant pool index and code offset: new constants are appended after the existing ones, and everything else is
/// rewritten in place at the same length. Code, exception tables, stack map frames and other attributes therefore stay valid without being recomputed. The exceptions are
/// <see cref="ReplaceMethodBody"/> and the method advice that <c>ClassPatch</c> adds, which move a method's code, byte for byte, into a new method and give the old method a short straight-line body,
/// <see cref="AddMethod(int, string, string, Func{CodeBuilder, CodeBuilder})"/>, which appends a method after the existing ones, and the entries that a lambda appends to the class's <c>BootstrapMethods</c> and <c>InnerClasses</c> attributes, which come after everything else.
/// </summary>
public sealed class ClassFileEditor : IConstantPool
{
    private const int StaticFlag = 0x0008;
    private const int NativeFlag = 0x0100;
    private const int AbstractFlag = 0x0400;

    // From class file version 51, the verifier needs a stack map frame at every branch target.
    private const int FramesVersion = 51;

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

    /// <summary>Adds a Class constant once. An existing one that names the class is reused, whichever Utf8 entry holds the name, so a pool with duplicate entries gets no third.</summary>
    /// <param name="name">The internal class name.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddClass(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Find<ClassEntry>(entry => IsUtf8(entry.NameIndex, name)) ?? Atomically(() => Append(ConstantEncoding.Reference(ConstantTag.Class, AddUtf8(name)), slots: 1));
    }

    /// <summary>Adds a String constant once. An existing one with the value is reused, whichever Utf8 entry holds it.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Find<StringEntry>(entry => IsUtf8(entry.StringIndex, value)) ?? Atomically(() => Append(ConstantEncoding.Reference(ConstantTag.String, AddUtf8(value)), slots: 1));
    }

    /// <summary>Adds a NameAndType constant once. An existing one with the name and descriptor is reused, whichever Utf8 entries hold them.</summary>
    /// <param name="name">The member name.</param>
    /// <param name="descriptor">The member descriptor.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddNameAndType(string name, string descriptor)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(descriptor);
        return Find<NameAndTypeEntry>(entry => IsNameAndType(entry.Index, name, descriptor))
            ?? Atomically(() => Append(ConstantEncoding.Reference(ConstantTag.NameAndType, AddUtf8(name), AddUtf8(descriptor)), slots: 1));
    }

    /// <summary>Adds a field reference once.</summary>
    /// <param name="owner">The internal name of the owner.</param>
    /// <param name="name">The name the field is declared with.</param>
    /// <param name="descriptor">Its type in JVM notation, for example <c>I</c> or <c>Ljava/lang/String;</c>.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddFieldReference(string owner, string name, string descriptor) => AddMemberReference(ConstantTag.Fieldref, owner, name, descriptor);

    /// <summary>Adds a method reference of a class once.</summary>
    /// <param name="owner">The internal name of the owner.</param>
    /// <param name="name">The name the method is declared with, for example <c>toString</c>.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>(I)Ljava/lang/String;</c>, which tell overloads apart.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddMethodReference(string owner, string name, string descriptor) => AddMemberReference(ConstantTag.Methodref, owner, name, descriptor);

    /// <summary>Adds a method reference of an interface once.</summary>
    /// <param name="owner">The internal name of the interface.</param>
    /// <param name="name">The name the method is declared with, for example <c>toString</c>.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>(I)Ljava/lang/String;</c>, which tell overloads apart.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddInterfaceMethodReference(string owner, string name, string descriptor) => AddMemberReference(ConstantTag.InterfaceMethodref, owner, name, descriptor);

    /// <summary>Adds an Integer constant once.</summary>
    /// <param name="value">The number the constant pool entry holds; an equal entry that exists is reused.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddInteger(int value) => Find<IntegerEntry>(entry => entry.Value == value) ?? Append(ConstantEncoding.Integer(value), slots: 1);

    /// <summary>Adds a Float constant once.</summary>
    /// <param name="value">The number the constant pool entry holds; an equal entry that exists is reused.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddFloat(float value) => Find<FloatEntry>(entry => BitConverter.SingleToInt32Bits(entry.Value) == BitConverter.SingleToInt32Bits(value)) ?? Append(ConstantEncoding.Float(value), slots: 1);

    /// <summary>Adds a Long constant once. It occupies two slots.</summary>
    /// <param name="value">The number the constant pool entry holds; an equal entry that exists is reused.</param>
    /// <returns>The index of the new or existing constant.</returns>
    public int AddLong(long value) => Find<LongEntry>(entry => entry.Value == value) ?? Append(ConstantEncoding.Long(value), slots: 2);

    /// <summary>Adds a Double constant once. It occupies two slots.</summary>
    /// <param name="value">The number the constant pool entry holds; an equal entry that exists is reused.</param>
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

    /// <summary>
    /// Makes code construct another class: <c>new A; dup; …; invokespecial A.&lt;init&gt;(D)</c> becomes <c>new B … B.&lt;init&gt;(D)</c>, with the same arguments and at the same length. B needs a constructor with the same descriptor, and the object
    /// must fit wherever the code uses it, such as the type of the field it is stored in; the verifier checks that when the class loads. A construction nested in the arguments of another of the same class is matched to its own constructor call.
    /// </summary>
    /// <param name="fromClass">The internal name of the class the code constructs now, for example <c>java/util/ArrayList</c>.</param>
    /// <param name="toClass">The internal name of the class to construct instead, for example <c>java/util/concurrent/CopyOnWriteArrayList</c>.</param>
    /// <param name="storedIn">Limits the edit to constructions whose object goes straight into a field of this name with <c>putfield</c> or <c>putstatic</c>, or <see langword="null"/> for every construction.</param>
    /// <param name="methodName">Limits the search to methods with this name, or <see langword="null"/> for every method.</param>
    /// <param name="methodDescriptor">Limits the search to methods with this descriptor, or <see langword="null"/> for any.</param>
    /// <returns>The number of constructions replaced.</returns>
    public int ReplaceConstruction(string fromClass, string toClass, string? storedIn = null, string? methodName = null, string? methodDescriptor = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fromClass);
        ArgumentException.ThrowIfNullOrEmpty(toClass);

        return Atomically(() =>
        {
            List<Construction> found = FindConstructions(fromClass, storedIn, methodName, methodDescriptor);

            if (found.Count == 0)
                return 0;

            // Constants first: each one appended moves the code that follows it in the file.
            int type = AddClass(toClass);
            Dictionary<string, int> constructors = [];

            foreach (string descriptor in found.Select(static site => site.ConstructorDescriptor).Distinct(StringComparer.Ordinal))
                constructors[descriptor] = AddMethodReference(toClass, name: "<init>", descriptor);

            List<Construction> sites = FindConstructions(fromClass, storedIn, methodName, methodDescriptor);

            foreach (Construction site in sites)
            {
                WriteUInt16(site.New + 1, type);
                WriteUInt16(site.Constructor + 1, constructors[site.ConstructorDescriptor]);
            }

            return sites.Count;
        });
    }

    /// <summary>
    /// Makes code get an object from a static factory instead of constructing it with no arguments: <c>new A; dup; invokespecial A.&lt;init&gt;()V</c>, seven bytes, becomes <c>invokestatic factory</c> and four <c>nop</c>s. The factory takes no parameters and
    /// returns a reference; the verifier checks that what it returns fits where the code uses the object.
    /// </summary>
    /// <param name="fromClass">The internal name of the class the code constructs now, for example <c>java/util/HashSet</c>.</param>
    /// <param name="factory">The factory, described with <see cref="JavaMethod.Static"/>, for example <c>java/util/concurrent/ConcurrentHashMap.newKeySet()Ljava/util/concurrent/ConcurrentHashMap$KeySetView;</c>.</param>
    /// <param name="storedIn">Limits the edit to constructions whose object goes straight into a field of this name with <c>putfield</c> or <c>putstatic</c>, or <see langword="null"/> for every construction.</param>
    /// <param name="methodName">Limits the search to methods with this name, or <see langword="null"/> for every method.</param>
    /// <param name="methodDescriptor">Limits the search to methods with this descriptor, or <see langword="null"/> for any.</param>
    /// <param name="ownerIsInterface">Whether the factory's owner is an interface, whose static methods need an interface method reference (class files of version 52 or later).</param>
    /// <returns>The number of constructions replaced.</returns>
    /// <exception cref="ArgumentException">The factory is not static, takes parameters or returns no reference, or a field the object goes into holds a primitive or an array type that the factory does not return.</exception>
    public int ReplaceConstruction(string fromClass, JavaMethod factory, string? storedIn = null, string? methodName = null, string? methodDescriptor = null, bool ownerIsInterface = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(fromClass);
        string result = FactoryResult(factory);

        return Atomically(() =>
        {
            List<Construction> found = [.. FindConstructions(fromClass, storedIn, methodName, methodDescriptor).Where(IsWithoutArguments)];

            if (found.Count == 0)
                return 0;

            foreach (Construction site in found)
            {
                if (site.Field is { } field && Incompatible(field.Descriptor, result))
                    throw new ArgumentException($"{factory} returns {result}, which the field {field.Name} of type {field.Descriptor} cannot hold.", nameof(factory));
            }

            int target = ownerIsInterface ? AddInterfaceMethodReference(factory.Owner, factory.Name, factory.Descriptor) : AddMethodReference(factory.Owner, factory.Name, factory.Descriptor);
            List<Construction> sites = [.. FindConstructions(fromClass, storedIn, methodName, methodDescriptor).Where(IsWithoutArguments)];

            foreach (Construction site in sites)
            {
                _bytes[site.New] = Opcodes.InvokeStatic;
                WriteUInt16(site.New + 1, target);
                _bytes.AsSpan(site.New + 3, length: 4).Fill(Opcodes.Nop);
            }

            _model = null;

            return sites.Count;
        });
    }

    /// <summary>
    /// Removes calls of a method that returns its own receiver, such as a fluent setter: the call becomes <c>nop</c>s, and the receiver it leaves on the stack stands for the result the call would have returned. The method must take no arguments and return
    /// the class it is called on, so nothing else on the stack changes.
    /// </summary>
    /// <param name="call">The call to remove, described with <see cref="JavaMethod.Virtual"/> or <see cref="JavaMethod.Interface"/>, for example <c>Builder.autosave()LBuilder;</c>.</param>
    /// <param name="methodName">Limits the search to methods with this name, or <see langword="null"/> for every method.</param>
    /// <param name="methodDescriptor">Limits the search to methods with this descriptor, or <see langword="null"/> for any.</param>
    /// <returns>The number of calls removed.</returns>
    /// <exception cref="ArgumentException">The call is static, takes arguments, or returns something other than its owner.</exception>
    public int RemoveInvocation(JavaMethod call, string? methodName = null, string? methodDescriptor = null)
    {
        if (RemovalProblem(call) is { } problem)
            throw new ArgumentException(problem, nameof(call));

        return Atomically(() =>
        {
            List<int> sites = FindInvocations(call, methodName, methodDescriptor);

            foreach (int site in sites)
                _bytes.AsSpan(site, _bytes[site] == Opcodes.InvokeInterface ? 5 : 3).Fill(Opcodes.Nop);

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
    /// <param name="methodName">The name the method is declared with, for example <c>toString</c>.</param>
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

    /// <summary>
    /// Writes instructions with a <see cref="CodeBuilder"/>, adding the constants they name to this class unless one that resolves to the same member or value exists, and returns their bytes, for <see cref="ReplaceCode"/> and for comparing with a method's code. The method's <c>max_stack</c> is not raised, so
    /// code put in place of other code must not need a deeper stack than what it replaces. Branches are refused: they would need stack map frames that this editor does not write.
    /// </summary>
    /// <param name="code">Writes the instructions and returns the builder.</param>
    /// <returns>The bytecode.</returns>
    /// <exception cref="ArgumentException">The instructions branch.</exception>
    public byte[] AssembleCode(Func<CodeBuilder, CodeBuilder> code)
    {
        ArgumentNullException.ThrowIfNull(code);

        // The stack starts deep enough for instructions that consume values the replaced code left, which only the caller can know about.
        CodeBuilder builder = new(this, parameterSlots: 0, initialDepth: ushort.MaxValue);
        CodeBuilder.RequireSame(builder, code(builder));

        return builder.HasBranches ? throw new ArgumentException("Assembled code cannot branch: a branch target would need a stack map frame, which the editor does not write.", nameof(code)) : builder.ToArray();
    }

    /// <summary>Makes String constants with one value point at another value. Every use of the constant changes.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The constant that the code loads instead, wherever it loaded the old one.</param>
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
    /// <param name="newValue">The constant that the code loads instead, wherever it loaded the old one.</param>
    /// <returns>The number of constants changed.</returns>
    public int ReplaceInteger(int oldValue, int newValue) => Rewrite<IntegerEntry>(entry => entry.Value == oldValue, offset => BinaryPrimitives.WriteInt32BigEndian(_bytes.AsSpan(offset), newValue));

    /// <summary>Changes a Float constant.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The constant that the code loads instead, wherever it loaded the old one.</param>
    /// <returns>The number of constants changed.</returns>
    public int ReplaceFloat(float oldValue, float newValue) => Rewrite<FloatEntry>(entry => BitConverter.SingleToInt32Bits(entry.Value) == BitConverter.SingleToInt32Bits(oldValue), offset => BinaryPrimitives.WriteInt32BigEndian(_bytes.AsSpan(offset), BitConverter.SingleToInt32Bits(newValue)));

    /// <summary>Changes a Long constant.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The constant that the code loads instead, wherever it loaded the old one.</param>
    /// <returns>The number of constants changed.</returns>
    public int ReplaceLong(long oldValue, long newValue) => Rewrite<LongEntry>(entry => entry.Value == oldValue, offset => BinaryPrimitives.WriteInt64BigEndian(_bytes.AsSpan(offset), newValue));

    /// <summary>Changes a Double constant.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The constant that the code loads instead, wherever it loaded the old one.</param>
    /// <returns>The number of constants changed.</returns>
    public int ReplaceDouble(double oldValue, double newValue) => Rewrite<DoubleEntry>(entry => BitConverter.DoubleToInt64Bits(entry.Value) == BitConverter.DoubleToInt64Bits(oldValue), offset => BinaryPrimitives.WriteInt64BigEndian(_bytes.AsSpan(offset), BitConverter.DoubleToInt64Bits(newValue)));

    /// <summary>Replaces the class access flags.</summary>
    /// <param name="accessFlags">The <c>ACC_</c> bits that replace the current ones, for example <c>0x0001</c> (public) to open it to other classes.</param>
    public void SetAccess(int accessFlags)
    {
        WriteUInt16(Model.AccessFlagsOffset, accessFlags);
        _model = null;
    }

    /// <summary>Replaces the access flags of matching fields.</summary>
    /// <param name="name">The name the field is declared with.</param>
    /// <param name="descriptor">The field descriptor, or <see langword="null"/> to match any.</param>
    /// <param name="accessFlags">The <c>ACC_</c> bits that replace the current ones, for example <c>0x0001</c> (public) to open it to other classes.</param>
    /// <returns>The number of fields changed.</returns>
    public int SetFieldAccess(string name, string? descriptor, int accessFlags) => SetMemberAccess(Model.Fields, name, descriptor, accessFlags);

    /// <summary>Replaces the access flags of matching methods.</summary>
    /// <param name="name">The name the method is declared with, for example <c>toString</c>.</param>
    /// <param name="descriptor">The method descriptor, or <see langword="null"/> to match every overload.</param>
    /// <param name="accessFlags">The <c>ACC_</c> bits that replace the current ones, for example <c>0x0001</c> (public) to open it to other classes.</param>
    /// <returns>The number of methods changed.</returns>
    public int SetMethodAccess(string name, string? descriptor, int accessFlags) => SetMemberAccess(Model.Methods, name, descriptor, accessFlags);

    /// <summary>
    /// Moves the code of a method into a new private synthetic method and gives the method code that a <see cref="CodeBuilder"/> writes, which may call the moved code, hand it to another thread as a
    /// <see cref="CodeBuilder.Lambda"/>, or skip it. The moved method keeps the descriptor and is <c>static</c> when the method is; the method keeps its flags, signature and annotations. A loaded class cannot gain
    /// methods, so this is only for a class that has not been defined yet. The new code is straight-line: branches would need stack map frames, which the editor does not write.
    /// </summary>
    /// <param name="name">The name the method is declared with, for example <c>clear</c>.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>()V</c>, which tell overloads apart.</param>
    /// <param name="movedBodyName">The name of the new method that holds the old code, for example <c>agent$clear</c>.</param>
    /// <param name="code">Writes the method's new code and returns the builder; the receiver and the parameters are in the first local variables, as in the old code.</param>
    /// <exception cref="ArgumentException">The new code branches.</exception>
    /// <exception cref="ClassFormatException">The class has no such method, or it has no code.</exception>
    /// <exception cref="InvalidOperationException">The class already declares a method with the moved body's name and the descriptor.</exception>
    public void ReplaceMethodBody(string name, string descriptor, string movedBodyName, Func<CodeBuilder, CodeBuilder> code)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(descriptor);
        ArgumentException.ThrowIfNullOrEmpty(movedBodyName);
        ArgumentNullException.ThrowIfNull(code);

        Atomically(() =>
        {
            bool isStatic = (RequireMethod(name, descriptor).AccessFlags & 0x0008) != 0;

            // The moved method exists before the new code is written, so a lambda that names it gets the handle a private method needs.
            CopyBody(name, descriptor, movedBodyName);

            CodeBuilder builder = new(this, Opcodes.CountArgumentSlots(descriptor) + (isStatic ? 0 : 1));
            CodeBuilder.RequireSame(builder, code(builder));

            if (builder.HasBranches)
                throw new ArgumentException("The new code cannot branch: a branch target would need a stack map frame, which the editor does not write.", nameof(code));

            ReplaceCodeAttribute(name, descriptor, new StubCode(builder.MaxStack, builder.MaxLocals, builder.ToArray()));
        });
    }

    /// <summary>
    /// Moves the code of a method into a new private synthetic method and gives the method the code of a stub that calls it. The method keeps its flags, signature and annotations; the new method is <c>static</c> when it is. It adds a method, so it is only valid for a
    /// class that has not been defined yet: a loaded class cannot gain methods.
    /// </summary>
    /// <param name="name">The name the method is declared with, for example <c>toString</c>.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>(I)Ljava/lang/String;</c>, which tell overloads apart.</param>
    /// <param name="bodyName">The name of the new method, which holds a verbatim copy of the old <c>Code</c> attribute.</param>
    /// <param name="stub">The code of the method from now on.</param>
    internal void ReplaceBodyWithStub(string name, string descriptor, string bodyName, StubCode stub)
    {
        Atomically(() =>
        {
            CopyBody(name, descriptor, bodyName);
            ReplaceCodeAttribute(name, descriptor, stub);
        });
    }

    /// <summary>
    /// Appends a method whose code a <see cref="CodeBuilder"/> writes against this class's constant pool, with the <c>max_stack</c> and <c>max_locals</c> it needs, for a helper that only the edited class can hold, such as one
    /// that names classes whose names change between releases. A loaded class cannot gain methods, so this is only for a class that has not been defined yet. The editor writes no stack map frames, so the code may
    /// branch only in a class of version 50 or older, which every JVM verifies without them.
    /// </summary>
    /// <param name="accessFlags">The <c>ACC_</c> bits, for example <c>0x100A</c> (private static synthetic). A method with code cannot be <c>abstract</c> or <c>native</c>.</param>
    /// <param name="name">The name the method is declared with, for example <c>agent$processPackets</c>.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>(Lpkg/Connection;)Z</c>.</param>
    /// <param name="code">Writes the code and returns the builder; the parameters are in the first local variables, after <c>this</c> for an instance method.</param>
    /// <exception cref="ArgumentException">The name, descriptor or flags are not those of a method with code, or the code branches in a class of version 51 or later.</exception>
    /// <exception cref="InvalidOperationException">The class already declares the method, or the code is not consistent: it pops more than the stack holds, has code no path reaches, or branches to a label it never places.</exception>
    public void AddMethod(int accessFlags, string name, string descriptor, Func<CodeBuilder, CodeBuilder> code)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(descriptor);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegative(accessFlags);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(accessFlags, ushort.MaxValue);

        if ((accessFlags & (AbstractFlag | NativeFlag)) != 0)
            throw new ArgumentException($"{name}{descriptor} has code, so it cannot be abstract or native.", nameof(accessFlags));

        if (name.AsSpan().IndexOfAny(".;[/<>") >= 0)
            throw new ArgumentException($"'{name}' is not a valid name for an added method.", nameof(name));

        int parameterSlots;

        try { parameterSlots = MethodDescriptor.Parse(descriptor).Parameters.Sum(MethodDescriptor.Slots); }
        catch (ClassFormatException exception) { throw new ArgumentException(exception.Message, nameof(descriptor), exception); }

        if (Model.FindMethod(name, descriptor) is not null)
            throw new InvalidOperationException($"The class already declares {name}{descriptor}.");

        Atomically(() =>
        {
            int major = Model.MajorVersion;
            CodeBuilder builder = new(this, parameterSlots + ((accessFlags & StaticFlag) == 0 ? 1 : 0));
            CodeBuilder.RequireSame(builder, code(builder));

            if (builder.HasBranches && major >= FramesVersion)
                throw new ArgumentException($"{name}{descriptor} branches, which a class file of version {major} only allows with stack map frames that the editor does not write.", nameof(code));

            AddMethod(accessFlags, name, descriptor, new StubCode(builder.MaxStack, builder.MaxLocals, builder.ToArray()));
        });
    }

    /// <summary>Appends a method with assembled code to the class. The class must not declare the method already.</summary>
    internal void AddMethod(int accessFlags, string name, string descriptor, StubCode code)
    {
        if (Model.FindMethod(name, descriptor) is not null)
            throw new InvalidOperationException($"The class already declares {name}{descriptor}.");

        Atomically(() =>
        {
            int nameIndex = AddUtf8(name);
            int descriptorIndex = AddUtf8(descriptor);
            int codeNameIndex = AddUtf8("Code");
            ReadOnlySpan<byte> original = _bytes;

            using MemoryStream result = new();

            int count = BinaryPrimitives.ReadUInt16BigEndian(original[Model.MethodsCountOffset..]);

            result.Write(original[..Model.MethodsCountOffset]);
            WriteUInt16To(result, count + 1);
            result.Write(original[(Model.MethodsCountOffset + 2)..Model.MethodsEnd]);
            WriteUInt16To(result, accessFlags);
            WriteUInt16To(result, nameIndex);
            WriteUInt16To(result, descriptorIndex);
            WriteUInt16To(result, value: 1);
            result.Write(AdviceStub.CodeAttribute(codeNameIndex, code));
            result.Write(original[Model.MethodsEnd..]);

            byte[] candidate = result.ToArray();

            _ = JavaClassFile.Read(candidate);
            _bytes = candidate;
            _model = null;
        });
    }

    /// <summary>Writes straight-line code with a builder against this class's constant pool, with the stack and local sizes it needs.</summary>
    internal StubCode Build(int parameterSlots, Func<CodeBuilder, CodeBuilder> code)
    {
        CodeBuilder builder = new(this, parameterSlots);
        CodeBuilder.RequireSame(builder, code(builder));

        return builder.HasBranches ? throw new InvalidOperationException(message: "Added methods are straight-line code.") : new StubCode(builder.MaxStack, builder.MaxLocals, builder.ToArray());
    }

    /// <summary>Produces the edited class file.</summary>
    /// <returns>A copy of the class file bytes.</returns>
    public byte[] ToArray() => (byte[])_bytes.Clone();

    int IConstantPool.MajorVersion => Model.MajorVersion;

    int IConstantPool.AddMethodHandle(int referenceKind, ConstantTag referenceTag, string owner, string name, string descriptor)
    {
        MemberReference wanted = new(referenceTag, owner, name, descriptor);

        return Find<MethodHandleEntry>(entry => entry.ReferenceKind == referenceKind && Model.ConstantPool.ElementAtOrDefault(entry.ReferenceIndex) is MemberReferenceEntry && Model.GetMemberReference(entry.ReferenceIndex) == wanted)
            ?? Atomically(() => Append(ConstantEncoding.MethodHandle(referenceKind, AddMemberReference(referenceTag, owner, name, descriptor)), slots: 1));
    }

    int IConstantPool.AddMethodType(string descriptor)
        => Find<MethodTypeEntry>(entry => IsUtf8(entry.DescriptorIndex, descriptor)) ?? Atomically(() => Append(ConstantEncoding.Reference(ConstantTag.MethodType, AddUtf8(descriptor)), slots: 1));

    int IConstantPool.AddInvokeDynamic(int bootstrapMethod, string name, string descriptor)
    {
        bool Matches(DynamicEntry entry) => entry.Kind == ConstantTag.InvokeDynamic && entry.BootstrapMethodIndex == bootstrapMethod && IsNameAndType(entry.NameAndTypeIndex, name, descriptor);

        return Find<DynamicEntry>(Matches) ?? Atomically(() => Append(ConstantEncoding.Reference(ConstantTag.InvokeDynamic, bootstrapMethod, AddNameAndType(name, descriptor)), slots: 1));
    }

    int IConstantPool.AddBootstrapMethod(int handle, IReadOnlyList<int> arguments)
    {
        return ClassAttributes.FindBootstrapMethod(_bytes, Model, handle, arguments) ?? Atomically(() =>
        {
            int nameIndex = AddUtf8(ClassAttributes.BootstrapMethods);
            int index = ClassAttributes.Count(_bytes, Model, ClassAttributes.BootstrapMethods);

            _bytes = ClassAttributes.Append(_bytes, Model, ClassAttributes.BootstrapMethods, nameIndex, ClassAttributes.BootstrapMethod(handle, arguments));
            _model = null;

            return index;
        });
    }

    void IConstantPool.AddInnerClass(string innerClass, string outerClass, string simpleName, int accessFlags)
    {
        if (ClassAttributes.ListsInnerClass(_bytes, Model, index => IsClass(index, innerClass)))
            return;

        Atomically(() =>
        {
            int nameIndex = AddUtf8(ClassAttributes.InnerClasses);
            byte[] entry = ClassAttributes.InnerClass(AddClass(innerClass), AddClass(outerClass), AddUtf8(simpleName), accessFlags);

            _bytes = ClassAttributes.Append(_bytes, Model, ClassAttributes.InnerClasses, nameIndex, entry);
            _model = null;
        });
    }

    bool IConstantPool.IsInterface(string className) => className == Model.Name && (Model.AccessFlags & 0x0200) != 0;

    bool IConstantPool.IsPrivateMethod(string owner, string name, string descriptor) => owner == Model.Name && (Model.FindMethod(name, descriptor)?.AccessFlags & 0x0002) is > 0;

    internal static string WithReceiver(string owner, string descriptor)
    {
        if (descriptor.Length == 0 || descriptor[0] != '(')
            throw new ArgumentException($"'{descriptor}' is not a method descriptor.", nameof(descriptor));
        string receiver = owner.StartsWith('[') ? owner : $"L{owner};";
        return string.Concat("(", receiver, descriptor.AsSpan(1));
    }

    // An existing reference is matched by what its Class and NameAndType entries resolve to, not by their indices: javac 8 can write two Class entries for one class, and the
    // code then uses a reference through the second one, which a lookup by the first Class entry's index would miss and duplicate.
    private int AddMemberReference(ConstantTag kind, string owner, string name, string descriptor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(descriptor);
        return Find<MemberReferenceEntry>(entry => entry.Kind == kind && IsClass(entry.ClassIndex, owner) && IsNameAndType(entry.NameAndTypeIndex, name, descriptor))
            ?? Atomically(() =>
            {
                int ownerIndex = AddClass(owner);
                int nameAndType = AddNameAndType(name, descriptor);
                return Append(ConstantEncoding.Reference(kind, ownerIndex, nameAndType), slots: 1);
            });
    }

    private bool IsClass(int index, string name) => Model.ConstantPool.ElementAtOrDefault(index) is ClassEntry entry && IsUtf8(entry.NameIndex, name);

    private bool IsNameAndType(int index, string name, string descriptor) => Model.ConstantPool.ElementAtOrDefault(index) is NameAndTypeEntry entry && IsUtf8(entry.NameIndex, name) && IsUtf8(entry.DescriptorIndex, descriptor);

    private bool IsUtf8(int index, string value) => Model.ConstantPool.ElementAtOrDefault(index) is Utf8Entry entry && entry.Value == value;

    // Appends a private synthetic method with the descriptor of a method and a verbatim copy of its Code attribute.
    private void CopyBody(string name, string descriptor, string bodyName)
    {
        if (Model.FindMethod(bodyName, descriptor) is not null)
            throw new InvalidOperationException($"The class already declares {bodyName}{descriptor}.");

        int bodyNameIndex = AddUtf8(bodyName);
        ClassMember method = RequireMethod(name, descriptor);

        if (method.Code is null)
            throw new ClassFormatException($"{name}{descriptor} has no code.");

        ReadOnlySpan<byte> original = _bytes;
        using MemoryStream result = new();

        result.Write(original[..Model.MethodsCountOffset]);
        WriteUInt16To(result, BinaryPrimitives.ReadUInt16BigEndian(original[Model.MethodsCountOffset..]) + 1);
        result.Write(original[(Model.MethodsCountOffset + 2)..Model.MethodsEnd]);
        WriteUInt16To(result, (method.AccessFlags & 0x0008) | 0x0002 | 0x1000);
        WriteUInt16To(result, bodyNameIndex);
        result.Write(original.Slice(method.AccessFlagsOffset + 4, length: 2));
        WriteUInt16To(result, value: 1);
        result.Write(original[method.CodeAttributeStart..method.CodeAttributeEnd]);
        result.Write(original[Model.MethodsEnd..]);

        Replace(result.ToArray());
    }

    // Gives a method a new Code attribute in place of its old one.
    private void ReplaceCodeAttribute(string name, string descriptor, StubCode code)
    {
        int codeNameIndex = AddUtf8("Code");
        ClassMember method = RequireMethod(name, descriptor);

        if (method.Code is null)
            throw new ClassFormatException($"{name}{descriptor} has no code.");

        ReadOnlySpan<byte> original = _bytes;
        using MemoryStream result = new();

        result.Write(original[..method.CodeAttributeStart]);
        result.Write(AdviceStub.CodeAttribute(codeNameIndex, code));
        result.Write(original[method.CodeAttributeEnd..]);

        Replace(result.ToArray());
    }

    private void Replace(byte[] candidate)
    {
        _ = JavaClassFile.Read(candidate);
        _bytes = candidate;
        _model = null;
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

    /// <summary>Tells why a call cannot be removed, or <see langword="null"/> when it can: it must have a receiver, take no arguments and return its own owner.</summary>
    internal static string? RemovalProblem(JavaMethod call)
    {
        if (call.Kind == JavaMethodKind.Static)
            return $"{call} is static, so there is no receiver to stand for its result";

        string fluent = "()L" + call.Owner + ";";

        return call.Descriptor == fluent ? null : $"{call} must take no arguments and return its own class ({fluent}), so that its receiver can stand for its result";
    }

    /// <summary>Gets the type a factory returns, checking that it is static, takes no parameters and returns a reference.</summary>
    internal static string FactoryResult(JavaMethod factory)
    {
        if (factory.Kind != JavaMethodKind.Static)
            throw new ArgumentException($"The factory {factory} must be static; describe it with JavaMethod.Static.", nameof(factory));

        bool returnsReference = factory.Descriptor.StartsWith("()", StringComparison.Ordinal) && factory.Descriptor.Length > 3 && factory.Descriptor[2] is 'L' or '[';

        return returnsReference ? factory.Descriptor[2..] : throw new ArgumentException($"The factory {factory} must take no parameters and return an object.", nameof(factory));
    }

    // A field of a class type may hold whatever the factory returns, which the verifier checks; a primitive never does, and an array only an array of the same type.
    private static bool Incompatible(string field, string result) => field[0] is not ('L' or '[') || ((field[0] == '[' || result[0] == '[') && field != result);

    private static bool IsWithoutArguments(Construction site) => site.Constructor == site.New + 4 && site.ConstructorDescriptor == "()V";

    private List<Construction> FindConstructions(string fromClass, string? storedIn, string? methodName, string? methodDescriptor)
    {
        List<Construction> sites = [];

        foreach (ClassMember method in Model.Methods)
        {
            if (method.Code is null || (methodName is not null && method.Name != methodName) || (methodDescriptor is not null && method.Descriptor != methodDescriptor))
                continue;

            IReadOnlyList<Instruction> instructions = method.Code.Instructions;

            for (int index = 0; index < instructions.Count; index++)
            {
                if (instructions[index].Opcode != Opcodes.New || !Model.ReferencesClass(instructions[index], fromClass))
                    continue;

                if (FindConstructor(instructions, index, fromClass) is not (int call, string descriptor))
                    continue;

                MemberReference? field = StoredField(instructions, call + 1);

                if (storedIn is not null && field?.Name != storedIn)
                    continue;

                sites.Add(new Construction(method.Code.CodeOffset + instructions[index].Offset, method.Code.CodeOffset + instructions[call].Offset, descriptor, field));
            }
        }

        return sites;
    }

    // The constructor call that initializes the object a new instruction created: the first one of the same class that is not matched by a nested new of that class first.
    private (int Index, string Descriptor)? FindConstructor(IReadOnlyList<Instruction> instructions, int start, string fromClass)
    {
        int nested = 0;

        for (int index = start + 1; index < instructions.Count; index++)
        {
            Instruction instruction = instructions[index];

            if (instruction.Opcode == Opcodes.New && Model.ReferencesClass(instruction, fromClass))
            {
                nested++;

                continue;
            }

            if (instruction.Opcode != Opcodes.InvokeSpecial || instruction.ConstantIndex is not int reference)
                continue;

            MemberReference constructor = Model.GetMemberReference(reference);

            if (constructor.Owner != fromClass || constructor.Name != "<init>")
                continue;

            if (nested == 0)
                return (index, constructor.Descriptor);

            nested--;
        }

        return null;
    }

    private MemberReference? StoredField(IReadOnlyList<Instruction> instructions, int index)
    {
        if (index >= instructions.Count || instructions[index] is not { Opcode: Opcodes.PutField or Opcodes.PutStatic, ConstantIndex: int reference })
            return null;

        return Model.GetMemberReference(reference);
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
                if (Model.Invokes(instruction, from))
                    sites.Add(method.Code.CodeOffset + instruction.Offset);
            }
        }
        return sites;
    }

    private void WriteUInt16(int offset, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ushort.MaxValue);
        BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan(offset), (ushort)value);
        _model = null;
    }

    /// <summary>A <c>new</c> instruction and the constructor call that initializes its object, at their offsets in the class file, with the field the object goes into, if any.</summary>
    private sealed record Construction(int New, int Constructor, string ConstructorDescriptor, MemberReference? Field);
}

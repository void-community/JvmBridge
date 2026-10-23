using System.Buffers.Binary;

namespace JvmBridge.ClassFiles.Code;

/// <summary>
/// Writes the bytecode of a method one instruction at a time, adding the constants each instruction names to the class's constant pool, so that no opcode or index is written by hand: <c>code =&gt; code.ALoad(0).Invoke(method).AReturn()</c>, a function that returns the builder it was given.
/// It tracks the operand stack and the local variables, so <see cref="ClassFileWriter.AddMethod(string, string, Func{CodeBuilder, CodeBuilder}, int)"/> fills in <c>max_stack</c> and <c>max_locals</c>, and it resolves the offsets of branches to
/// <see cref="CodeLabel"/>s. It writes no stack map frames, so a class file of version 51 or later cannot have branches written by it; see <see cref="ClassFileWriter.AddMethod(string, string, Func{CodeBuilder, CodeBuilder}, int)"/>.
/// </summary>
public sealed class CodeBuilder
{
    private readonly List<byte> _code = [];
    private readonly List<(int At, int From, CodeLabel Target)> _jumps = [];
    private readonly IConstantPool _pool;
    private int _depth;
    private bool _reachable = true;

    internal CodeBuilder(IConstantPool pool, int parameterSlots, int initialDepth = 0)
    {
        _pool = pool;
        MaxLocals = parameterSlots;
        _depth = initialDepth;
    }

    internal bool HasBranches => _jumps.Count > 0;

    internal int MaxLocals { get; private set; }

    internal int MaxStack { get; private set; }

    /// <summary><c>aconst_null</c>: pushes the null reference.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder AConstNull() => Op(Opcodes.AConstNull);

    /// <summary>Pushes the reference in a local variable, with <c>aload_0</c> to <c>aload_3</c>, <c>aload</c> or <c>wide aload</c>.</summary>
    /// <param name="slot">The local variable: 0 is <c>this</c> in an instance method, and parameters follow in order.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder ALoad(int slot) => Local(Opcodes.ALoad0, Opcodes.ALoad, slot, size: 1, load: true);

    /// <summary><c>anewarray</c>: pops a length and pushes a new array of references of a class.</summary>
    /// <param name="className">The internal name of the element class, for example <c>java/lang/String</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder ANewArray(string className) => WithIndex(Opcodes.ANewArray, _pool.AddClass(className), pops: 1, pushes: 1);

    /// <summary><c>areturn</c>: returns the reference on top of the stack to the caller.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder AReturn() => Op(Opcodes.AReturn);

    /// <summary><c>arraylength</c>: pops an array and pushes its length.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder ArrayLength() => Op(Opcodes.ArrayLength);

    /// <summary>Pops a reference into a local variable, with <c>astore_0</c> to <c>astore_3</c>, <c>astore</c> or <c>wide astore</c>.</summary>
    /// <param name="slot">The local variable to store into.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder AStore(int slot) => Local(Opcodes.AStore0, Opcodes.AStore, slot, size: 1, load: false);

    /// <summary><c>athrow</c>: throws the exception object on top of the stack.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder AThrow() => Op(Opcodes.AThrow);

    /// <summary><c>checkcast</c>: fails with <c>ClassCastException</c> unless the reference on top of the stack is null or of a class, and lets the verifier treat it as that class.</summary>
    /// <param name="className">The internal name of the class, for example <c>java/util/List</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder CheckCast(string className) => WithIndex(Opcodes.CheckCast, _pool.AddClass(className), pops: 1, pushes: 1);

    /// <summary>Pushes a double constant, with <c>dconst_0</c>, <c>dconst_1</c> or <c>ldc2_w</c>.</summary>
    /// <param name="value">The number to push.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder DConst(double value)
    {
        if (BitConverter.DoubleToInt64Bits(value) == 0)
            return Op(Opcodes.DConst0);

        return value == 1.0 ? Op(Opcodes.DConst1) : WithIndex(Opcodes.Ldc2W, _pool.AddDouble(value), pops: 0, pushes: 2);
    }

    /// <summary>Pushes the double in a local variable, which takes it and the next slot.</summary>
    /// <param name="slot">The first of the two local variable slots.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder DLoad(int slot) => Local(Opcodes.DLoad0, Opcodes.DLoad, slot, size: 2, load: true);

    /// <summary><c>dreturn</c>: returns the double on top of the stack to the caller.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder DReturn() => Op(Opcodes.DReturn);

    /// <summary>Pops a double into a local variable and the slot after it.</summary>
    /// <param name="slot">The first of the two local variable slots.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder DStore(int slot) => Local(Opcodes.DStore0, Opcodes.DStore, slot, size: 2, load: false);

    /// <summary><c>dup</c>: pushes a copy of the value on top of the stack, as after <see cref="New"/> before the constructor call.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder Dup() => Op(Opcodes.Dup);

    /// <summary>Pushes a float constant, with <c>fconst_0</c> to <c>fconst_2</c> or <c>ldc</c>.</summary>
    /// <param name="value">The number to push.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder FConst(float value)
    {
        if (BitConverter.SingleToInt32Bits(value) == 0)
            return Op(Opcodes.FConst0);

        if (value is 1.0f or 2.0f)
            return Op(value == 1.0f ? Opcodes.FConst1 : Opcodes.FConst2);

        return Constant(_pool.AddFloat(value));
    }

    /// <summary>Pushes the float in a local variable.</summary>
    /// <param name="slot">The local variable to read.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder FLoad(int slot) => Local(Opcodes.FLoad0, Opcodes.FLoad, slot, size: 1, load: true);

    /// <summary><c>freturn</c>: returns the float on top of the stack to the caller.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder FReturn() => Op(Opcodes.FReturn);

    /// <summary>Pops a float into a local variable.</summary>
    /// <param name="slot">The local variable to store into.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder FStore(int slot) => Local(Opcodes.FStore0, Opcodes.FStore, slot, size: 1, load: false);

    /// <summary><c>getfield</c>: pops an object and pushes the value of one of its fields.</summary>
    /// <param name="owner">The internal name of the class that declares the field, for example <c>com/acme/Service</c>.</param>
    /// <param name="name">The name the field is declared with.</param>
    /// <param name="descriptor">Its type in JVM notation, for example <c>I</c> or <c>Ljava/lang/String;</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder GetField(string owner, string name, string descriptor) => WithIndex(Opcodes.GetField, _pool.AddFieldReference(owner, name, descriptor), pops: 1, Slots(descriptor));

    /// <summary><c>getstatic</c>: pushes the value of a static field.</summary>
    /// <param name="owner">The internal name of the class that declares the field, for example <c>java/lang/System</c>.</param>
    /// <param name="name">The name the field is declared with.</param>
    /// <param name="descriptor">Its type in JVM notation, for example <c>Ljava/io/PrintStream;</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder GetStatic(string owner, string name, string descriptor) => WithIndex(Opcodes.GetStatic, _pool.AddFieldReference(owner, name, descriptor), pops: 0, Slots(descriptor));

    /// <summary><c>goto</c>: jumps to a label.</summary>
    /// <param name="target">Where to continue.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder Goto(CodeLabel target) => Branch(Opcodes.Goto, target);

    /// <summary>Pushes an int constant in the shortest form: <c>iconst_m1</c> to <c>iconst_5</c>, <c>bipush</c>, <c>sipush</c> or <c>ldc</c>.</summary>
    /// <param name="value">The number to push.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder IConst(int value)
    {
        if (value is >= -1 and <= 5)
            return Op((byte)(Opcodes.IConst0 + value));

        if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
            return Emit([Opcodes.BIPush, (byte)value], pops: 0, pushes: 1);

        if (value is >= short.MinValue and <= short.MaxValue)
            return Emit([Opcodes.SIPush, (byte)(value >> 8), (byte)value], pops: 0, pushes: 1);

        return Constant(_pool.AddInteger(value));
    }

    /// <summary>Pops one value, or two for <c>if_icmp</c> and <c>if_acmp</c>, and jumps to a label when the condition of the branch instruction holds.</summary>
    /// <param name="opcode">A conditional branch: <see cref="Opcodes.IfEq"/> to <see cref="Opcodes.IfACmpNe"/>, <see cref="Opcodes.IfNull"/> or <see cref="Opcodes.IfNonNull"/>.</param>
    /// <param name="target">Where to continue when the condition holds.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder If(byte opcode, CodeLabel target)
    {
        bool conditional = opcode is >= Opcodes.IfEq and <= Opcodes.IfACmpNe or Opcodes.IfNull or Opcodes.IfNonNull;

        return conditional ? Branch(opcode, target) : throw new ArgumentException($"0x{opcode:x2} is not a conditional branch.", nameof(opcode));
    }

    /// <summary><c>ifnonnull</c>: pops a reference and jumps to a label unless it is null.</summary>
    /// <param name="target">Where to continue when the reference is not null.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder IfNonNull(CodeLabel target) => Branch(Opcodes.IfNonNull, target);

    /// <summary><c>ifnull</c>: pops a reference and jumps to a label when it is null.</summary>
    /// <param name="target">Where to continue when the reference is null.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder IfNull(CodeLabel target) => Branch(Opcodes.IfNull, target);

    /// <summary>Adds a constant to an int local variable, with <c>iinc</c> or <c>wide iinc</c>.</summary>
    /// <param name="slot">The int local variable to change.</param>
    /// <param name="delta">How much to add; negative to subtract.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder IInc(int slot, int delta)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, short.MinValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delta, short.MaxValue);
        MaxLocals = Math.Max(MaxLocals, slot + 1);

        bool narrow = slot <= byte.MaxValue && delta is >= sbyte.MinValue and <= sbyte.MaxValue;

        return narrow
            ? Emit([Opcodes.IInc, (byte)slot, (byte)delta], pops: 0, pushes: 0)
            : Emit([Opcodes.Wide, Opcodes.IInc, (byte)(slot >> 8), (byte)slot, (byte)(delta >> 8), (byte)delta], pops: 0, pushes: 0);
    }

    /// <summary>Pushes the int in a local variable; booleans, bytes, chars and shorts are ints there too.</summary>
    /// <param name="slot">The local variable to read.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder ILoad(int slot) => Local(Opcodes.ILoad0, Opcodes.ILoad, slot, size: 1, load: true);

    /// <summary><c>instanceof</c>: pops a reference and pushes 1 when it is an instance of a class, 0 otherwise.</summary>
    /// <param name="className">The internal name of the class, for example <c>java/util/List</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder InstanceOf(string className) => WithIndex(Opcodes.InstanceOf, _pool.AddClass(className), pops: 1, pushes: 1);

    /// <summary>
    /// Calls a method with the instruction of its kind: <c>invokestatic</c>, <c>invokevirtual</c> or <c>invokeinterface</c>. It pops the receiver, unless the method is static, and the arguments, and pushes the result unless it is <c>void</c>.
    /// </summary>
    /// <param name="method">The method, for example <c>JavaMethod.Virtual("java/lang/String", "length", "()I")</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder Invoke(JavaMethod method)
    {
        int arguments = Opcodes.CountArgumentSlots(method.Descriptor);
        int result = ReturnSlots(method.Descriptor);

        if (method.Kind == JavaMethodKind.Static)
            return WithIndex(Opcodes.InvokeStatic, _pool.AddMethodReference(method.Owner, method.Name, method.Descriptor), arguments, result);

        if (method.Kind == JavaMethodKind.Virtual)
            return WithIndex(Opcodes.InvokeVirtual, _pool.AddMethodReference(method.Owner, method.Name, method.Descriptor), arguments + 1, result);

        int reference = _pool.AddInterfaceMethodReference(method.Owner, method.Name, method.Descriptor);

        return Emit([Opcodes.InvokeInterface, (byte)(reference >> 8), (byte)reference, (byte)(arguments + 1), 0], arguments + 1, result);
    }

    /// <summary><c>invokespecial</c>: calls a constructor, a private method or a superclass method without virtual dispatch, popping the receiver and the arguments.</summary>
    /// <param name="owner">The internal name of the class that declares the method, for example <c>java/util/ArrayList</c>.</param>
    /// <param name="name">The method name, <c>&lt;init&gt;</c> for a constructor.</param>
    /// <param name="descriptor">Its parameter and return types in JVM notation, for example <c>()V</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder InvokeSpecial(string owner, string name, string descriptor)
        => WithIndex(Opcodes.InvokeSpecial, _pool.AddMethodReference(owner, name, descriptor), Opcodes.CountArgumentSlots(descriptor) + 1, ReturnSlots(descriptor));

    /// <summary><c>ireturn</c>: returns the int, boolean, byte, char or short on top of the stack to the caller.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder IReturn() => Op(Opcodes.IReturn);

    /// <summary>Pops an int, boolean, byte, char or short into a local variable.</summary>
    /// <param name="slot">The local variable to store into.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder IStore(int slot) => Local(Opcodes.IStore0, Opcodes.IStore, slot, size: 1, load: false);

    /// <summary>Pushes a long constant, with <c>lconst_0</c>, <c>lconst_1</c> or <c>ldc2_w</c>.</summary>
    /// <param name="value">The number to push.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder LConst(long value) => value is 0 or 1 ? Op((byte)(Opcodes.LConst0 + value)) : WithIndex(Opcodes.Ldc2W, _pool.AddLong(value), pops: 0, pushes: 2);

    /// <summary>Pushes a String constant, with <c>ldc</c> or <c>ldc_w</c>.</summary>
    /// <param name="value">The text, which the JVM interns.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder Ldc(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Constant(_pool.AddString(value));
    }

    /// <summary>Pushes a class literal, as <c>Foo.class</c> does, with <c>ldc</c> or <c>ldc_w</c>. A class file older than version 49 (Java 5) cannot load class literals this way.</summary>
    /// <param name="className">The internal name of the class, for example <c>java/lang/String</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder LdcClass(string className) => Constant(_pool.AddClass(className));

    /// <summary>Pushes the long in a local variable, which takes it and the next slot.</summary>
    /// <param name="slot">The first of the two local variable slots.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder LLoad(int slot) => Local(Opcodes.LLoad0, Opcodes.LLoad, slot, size: 2, load: true);

    /// <summary><c>lreturn</c>: returns the long on top of the stack to the caller.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder LReturn() => Op(Opcodes.LReturn);

    /// <summary>Pops a long into a local variable and the slot after it.</summary>
    /// <param name="slot">The first of the two local variable slots.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder LStore(int slot) => Local(Opcodes.LStore0, Opcodes.LStore, slot, size: 2, load: false);

    /// <summary>Puts a label at the next instruction. Code after a jump, return or throw can only be reached through a label that a branch targets.</summary>
    /// <param name="label">A label of this builder that is not placed yet.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder Mark(CodeLabel label)
    {
        ArgumentNullException.ThrowIfNull(label);
        RequireOwn(label);

        if (label.Offset >= 0)
            throw new InvalidOperationException("The label is placed already; make a new one for another place.");

        if (label.Depth is int expected)
        {
            if (_reachable && expected != _depth)
                throw new InvalidOperationException($"A branch reaches the label with {expected} stack slots, but the code before it has {_depth}.");

            _depth = expected;
        }
        else if (!_reachable)
        {
            throw new InvalidOperationException("No branch before the label targets it and the code before it does not continue, so its stack is unknown; branch to it first.");
        }

        label.Depth = _depth;
        label.Offset = _code.Count;
        _reachable = true;

        return this;
    }

    /// <summary><c>new</c>: pushes a new, not yet initialized object of a class. Follow it with <see cref="Dup"/>, the constructor's arguments and <see cref="InvokeSpecial"/> of <c>&lt;init&gt;</c>.</summary>
    /// <param name="className">The internal name of the class, for example <c>java/util/ArrayList</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder New(string className) => WithIndex(Opcodes.New, _pool.AddClass(className), pops: 0, pushes: 1);

    /// <summary><c>newarray</c>: pops a length and pushes a new array of a primitive type.</summary>
    /// <param name="elementType">The element type as a descriptor letter: <c>Z</c>, <c>C</c>, <c>F</c>, <c>D</c>, <c>B</c>, <c>S</c>, <c>I</c> or <c>J</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder NewArray(char elementType)
    {
        int code = "ZCFDBSIJ".IndexOf(elementType, StringComparison.Ordinal);

        return code < 0 ? throw new ArgumentException($"'{elementType}' is not a primitive type letter.", nameof(elementType)) : Emit([Opcodes.NewArray, (byte)(code + 4)], pops: 1, pushes: 1);
    }

    /// <summary>Makes a label to branch to; <see cref="Mark"/> places it.</summary>
    /// <returns>The label.</returns>
    public CodeLabel NewLabel() => new(this);

    /// <summary><c>nop</c>: does nothing.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder Nop() => Op(Opcodes.Nop);

    /// <summary>Writes an instruction of one byte that has no operands, such as <see cref="Opcodes.IAdd"/>, <see cref="Opcodes.Swap"/> or <see cref="Opcodes.MonitorEnter"/>.</summary>
    /// <param name="opcode">The instruction.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder Op(byte opcode)
    {
        (int Pops, int Pushes) effect = Opcodes.GetStackEffect(opcode) ?? throw new ArgumentException($"0x{opcode:x2} takes operands; write it with the method of its name.", nameof(opcode));
        Write([opcode], effect.Pops, effect.Pushes);

        if (opcode is >= Opcodes.IReturn and <= Opcodes.Return or Opcodes.AThrow)
            _reachable = false;

        return this;
    }

    /// <summary><c>pop</c>: discards the value on top of the stack, such as a result the code does not use.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder Pop() => Op(Opcodes.Pop);

    /// <summary><c>putfield</c>: pops an object and a value and stores the value in one of its fields.</summary>
    /// <param name="owner">The internal name of the class that declares the field, for example <c>com/acme/Service</c>.</param>
    /// <param name="name">The name the field is declared with.</param>
    /// <param name="descriptor">Its type in JVM notation, for example <c>I</c> or <c>Ljava/lang/String;</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder PutField(string owner, string name, string descriptor) => WithIndex(Opcodes.PutField, _pool.AddFieldReference(owner, name, descriptor), 1 + Slots(descriptor), pushes: 0);

    /// <summary><c>putstatic</c>: pops a value into a static field.</summary>
    /// <param name="owner">The internal name of the class that declares the field, for example <c>com/acme/Service</c>.</param>
    /// <param name="name">The name the field is declared with.</param>
    /// <param name="descriptor">Its type in JVM notation, for example <c>I</c> or <c>Ljava/lang/String;</c>.</param>
    /// <returns>This builder.</returns>
    public CodeBuilder PutStatic(string owner, string name, string descriptor) => WithIndex(Opcodes.PutStatic, _pool.AddFieldReference(owner, name, descriptor), Slots(descriptor), pushes: 0);

    /// <summary><c>return</c>: returns from a <c>void</c> method.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder Return() => Op(Opcodes.Return);

    /// <summary><c>swap</c>: exchanges the top two values, neither a long nor a double.</summary>
    /// <returns>This builder.</returns>
    public CodeBuilder Swap() => Op(Opcodes.Swap);

    /// <summary>Checks that a function given a builder returned it, so instructions written to another builder are not silently lost.</summary>
    internal static void RequireSame(CodeBuilder given, CodeBuilder returned)
    {
        if (!ReferenceEquals(given, returned))
            throw new InvalidOperationException("The code function must return the builder it was given.");
    }

    /// <summary>Gets the bytes, with every branch pointing at its label.</summary>
    internal byte[] ToArray()
    {
        byte[] code = [.. _code];

        foreach ((int at, int from, CodeLabel target) in _jumps)
        {
            if (target.Offset < 0)
                throw new InvalidOperationException("A branch targets a label that was never placed; call Mark with it.");

            int offset = target.Offset - from;

            if (offset is < short.MinValue or > short.MaxValue)
                throw new InvalidOperationException($"A branch jumps {offset} bytes, further than a two-byte offset reaches.");

            BinaryPrimitives.WriteInt16BigEndian(code.AsSpan(at), (short)offset);
        }

        return code;
    }

    private static int ReturnSlots(string descriptor)
    {
        int close = descriptor.IndexOf(')', StringComparison.Ordinal);

        return close < 0 ? throw new ArgumentException($"'{descriptor}' is not a method descriptor.", nameof(descriptor)) : Slots(descriptor[(close + 1)..]);
    }

    private static int Slots(string type) => type switch
    {
        "V" => 0,
        "J" or "D" => 2,
        _ => 1,
    };

    private CodeBuilder Branch(byte opcode, CodeLabel target)
    {
        ArgumentNullException.ThrowIfNull(target);
        RequireOwn(target);

        int pops = opcode switch
        {
            Opcodes.Goto => 0,
            >= Opcodes.IfICmpEq and <= Opcodes.IfACmpNe => 2,
            _ => 1,
        };

        int from = _code.Count;
        Write([opcode, 0, 0], pops, pushes: 0);

        if (target.Depth is int expected && expected != _depth)
            throw new InvalidOperationException($"The branch reaches its label with {_depth} stack slots, but the label has {expected}.");

        target.Depth = _depth;
        _jumps.Add((from + 1, from, target));

        if (opcode == Opcodes.Goto)
            _reachable = false;

        return this;
    }

    private CodeBuilder Constant(int index) => index <= byte.MaxValue ? Emit([Opcodes.Ldc, (byte)index], pops: 0, pushes: 1) : WithIndex(Opcodes.LdcW, index, pops: 0, pushes: 1);

    private CodeBuilder Emit(ReadOnlySpan<byte> instruction, int pops, int pushes)
    {
        Write(instruction, pops, pushes);

        return this;
    }

    private void Write(ReadOnlySpan<byte> instruction, int pops, int pushes)
    {
        if (!_reachable)
            throw new InvalidOperationException($"The code at offset {_code.Count} follows a jump, return or throw and no label is placed there, so it can never run.");

        if (_depth < pops)
            throw new InvalidOperationException($"The instruction 0x{instruction[0]:x2} at offset {_code.Count} needs {pops} stack slots, but the stack has {_depth}.");

        _depth += pushes - pops;
        MaxStack = Math.Max(MaxStack, _depth);
        _code.AddRange(instruction);
    }

    private CodeBuilder Local(byte first, byte general, int slot, int size, bool load)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, ushort.MaxValue - 1);
        MaxLocals = Math.Max(MaxLocals, slot + size);

        int pops = load ? 0 : size;
        int pushes = load ? size : 0;

        if (slot <= 3)
            return Emit([(byte)(first + slot)], pops, pushes);

        return slot <= byte.MaxValue ? Emit([general, (byte)slot], pops, pushes) : Emit([Opcodes.Wide, general, (byte)(slot >> 8), (byte)slot], pops, pushes);
    }

    private void RequireOwn(CodeLabel label)
    {
        if (!ReferenceEquals(label.Owner, this))
            throw new ArgumentException("The label belongs to another builder.", nameof(label));
    }

    private CodeBuilder WithIndex(byte opcode, int index, int pops, int pushes) => Emit([opcode, (byte)(index >> 8), (byte)index], pops, pushes);
}

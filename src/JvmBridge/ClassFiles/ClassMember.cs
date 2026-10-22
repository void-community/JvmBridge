namespace JvmBridge.ClassFiles;

/// <summary>A single decoded instruction of a <c>Code</c> attribute.</summary>
/// <param name="Offset">The offset from the start of the method's code.</param>
/// <param name="Opcode">The opcode byte.</param>
/// <param name="Length">The instruction length in bytes, including operands and switch padding.</param>
/// <param name="ConstantIndex">The constant pool index the instruction references, or <see langword="null"/> when it has none.</param>
public readonly record struct Instruction(int Offset, byte Opcode, int Length, int? ConstantIndex);

/// <summary>A method's <c>Code</c> attribute. Stack map frames and other nested attributes are not interpreted.</summary>
public sealed class CodeAttribute
{
    internal CodeAttribute(int maxStack, int maxLocals, int codeOffset, int codeLength, IReadOnlyList<Instruction> instructions)
    {
        MaxStack = maxStack;
        MaxLocals = maxLocals;
        CodeOffset = codeOffset;
        CodeLength = codeLength;
        Instructions = instructions;
    }

    /// <summary>Gets the maximum operand stack depth.</summary>
    public int MaxStack { get; }

    /// <summary>Gets the number of local variable slots.</summary>
    public int MaxLocals { get; }

    /// <summary>Gets the offset of the first code byte in the class file that was read.</summary>
    public int CodeOffset { get; }

    /// <summary>Gets the length of the code in bytes.</summary>
    public int CodeLength { get; }

    /// <summary>Gets the decoded instructions in order.</summary>
    public IReadOnlyList<Instruction> Instructions { get; }
}

/// <summary>A declared field or method.</summary>
public sealed class ClassMember
{
    internal ClassMember(string name, string descriptor, int accessFlags, int accessFlagsOffset, CodeAttribute? code, int endOffset, int codeAttributeStart, int codeAttributeEnd)
    {
        Name = name;
        Descriptor = descriptor;
        AccessFlags = accessFlags;
        AccessFlagsOffset = accessFlagsOffset;
        Code = code;
        EndOffset = endOffset;
        CodeAttributeStart = codeAttributeStart;
        CodeAttributeEnd = codeAttributeEnd;
    }

    /// <summary>Gets the member name.</summary>
    public string Name { get; }

    /// <summary>Gets the member descriptor.</summary>
    public string Descriptor { get; }

    /// <summary>Gets the access flags.</summary>
    public int AccessFlags { get; }

    /// <summary>Gets the method's code, or <see langword="null"/> for fields and for abstract and native methods.</summary>
    public CodeAttribute? Code { get; }

    /// <summary>Gets the offset of the member: where its access flags start.</summary>
    internal int AccessFlagsOffset { get; }

    /// <summary>Gets the offset just after the member and all its attributes.</summary>
    internal int EndOffset { get; }

    /// <summary>Gets the offset of the <c>Code</c> attribute (its name index), or -1 without code.</summary>
    internal int CodeAttributeStart { get; }

    /// <summary>Gets the offset just after the <c>Code</c> attribute, or -1 without code.</summary>
    internal int CodeAttributeEnd { get; }
}

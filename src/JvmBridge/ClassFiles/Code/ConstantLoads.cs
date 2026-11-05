namespace JvmBridge.ClassFiles.Code;

/// <summary>
/// Points the <c>ldc</c>, <c>ldc_w</c> and <c>ldc2_w</c> instructions of one method, or of every overload of it, that load a constant at another value, at the same length. The old constant pool entry stays for the code outside the method.
/// </summary>
internal static class ConstantLoads
{
    /// <summary>Replaces the loads of a constant in a method.</summary>
    /// <param name="editor">The editor of the class. When this throws, the replacement's constant may have been added, so the caller discards the editor's result.</param>
    /// <param name="methodName">The name of the method whose loads change.</param>
    /// <param name="methodDescriptor">The method descriptor, or <see langword="null"/> for every overload.</param>
    /// <param name="isOld">Whether an entry of the class holds the value to replace.</param>
    /// <param name="addReplacement">Adds the replacement's constant, or finds an existing one, and returns its index.</param>
    /// <param name="replacement">The replacement, as the reason of a refusal names it.</param>
    /// <returns>The number of loads changed; none when the method loads no such constant, and then nothing is added.</returns>
    /// <exception cref="InvalidOperationException">An <c>ldc</c>, which reaches only the first 256 constants, loads the value, and the replacement's constant is further out.</exception>
    internal static int Replace(
        ClassFileEditor editor,
        string methodName,
        string? methodDescriptor,
        Func<JavaClassFile, ConstantPoolEntry, bool> isOld,
        Func<ClassFileEditor, int> addReplacement,
        string replacement
    )
    {
        JavaClassFile model = editor.Model;

        LoadSite[] loads = [.. model.Methods
            .Where(method => method.Name == methodName && (methodDescriptor is null || method.Descriptor == methodDescriptor))
            .SelectMany(method => (method.Code?.Instructions ?? [])
                .Where(instruction => IsLoad(instruction) && model.ConstantPool[instruction.ConstantIndex ?? 0] is { } entry && isOld(model, entry))
                .Select(instruction => new LoadSite(method.Descriptor, instruction.Offset, instruction.Length, instruction.Opcode)))];

        if (loads.Length == 0)
            return 0;

        int index = addReplacement(editor);

        LoadSite[] narrow = [.. loads.Where(static load => load.Opcode == Opcodes.Ldc)];

        if (index > byte.MaxValue && narrow.Length > 0)
        {
            throw new InvalidOperationException(
                $"{methodName}{narrow[0].Descriptor} loads the constant with an ldc at offset {narrow[0].Offset}, which reaches only the first 256 constants, and {replacement} would be constant {index}."
            );
        }

        // Adding the constant moved the code, so the bytes each load has now are read after it.
        model = editor.Model;
        byte[] bytes = editor.ToArray();

        foreach (LoadSite load in loads)
        {
            CodeAttribute code = model.FindMethod(methodName, load.Descriptor)?.Code ?? throw new ClassFormatException($"{methodName}{load.Descriptor} has no code.");
            byte[] expected = bytes.AsSpan(code.CodeOffset + load.Offset, load.Length).ToArray();
            byte[] changed = load.Opcode == Opcodes.Ldc ? [Opcodes.Ldc, (byte)index] : [load.Opcode, (byte)(index >> 8), (byte)index];

            if (!editor.ReplaceCode(methodName, load.Descriptor, load.Offset, expected, changed))
                throw new ClassFormatException($"The load at offset {load.Offset} of {methodName}{load.Descriptor} could not be replaced.");
        }

        return loads.Length;
    }

    private static bool IsLoad(Instruction instruction) => instruction is { Opcode: Opcodes.Ldc or Opcodes.LdcW or Opcodes.Ldc2W, ConstantIndex: not null };

    private readonly record struct LoadSite(string Descriptor, int Offset, int Length, byte Opcode);
}

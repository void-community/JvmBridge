namespace JvmBridge.ClassFiles.Advice;

/// <summary>
/// Builds the body that replaces an advised method: straight-line code that calls the before hook, calls the method's original code, which now lives in a private method of its own, and calls the after hook. It has no branches, so it needs no stack map frame at
/// any class file version.
/// </summary>
internal static class AdviceStub
{
    private const byte AconstNull = 0x01;
    private const byte ReturnVoid = 0xb1;

    /// <summary>Builds the code of the stub.</summary>
    /// <param name="isStatic">Whether the advised method is static, in which case the hooks receive <see langword="null"/> for the receiver.</param>
    /// <param name="method">The descriptor of the advised method.</param>
    /// <param name="bodyReference">The constant pool index of the method reference to the method that holds the original code.</param>
    /// <param name="beforeReference">The constant pool index of the before hook, or <see langword="null"/> for none.</param>
    /// <param name="afterReference">The constant pool index of the after hook, or <see langword="null"/> for none.</param>
    /// <param name="includeArguments">Whether the after hook receives the arguments of the call too.</param>
    /// <returns>The code and its sizes.</returns>
    internal static StubCode Build(bool isStatic, MethodDescriptor method, int bodyReference, int? beforeReference, int? afterReference, bool includeArguments)
    {
        List<byte> code = [];
        int receiverSlots = isStatic ? 0 : 1;
        int temporary = receiverSlots + method.ParameterSlots;
        int stack = receiverSlots + method.ParameterSlots;

        if (beforeReference is int before)
        {
            LoadReceiver(code, isStatic);
            LoadArguments(code, method, receiverSlots);
            Invoke(code, Opcodes.InvokeStatic, before);
            stack = Math.Max(stack, 1 + method.ParameterSlots);
        }

        if (!isStatic)
            Emit(code, shortForm: 0x2a, longForm: 0x19, slot: 0);

        LoadArguments(code, method, receiverSlots);
        Invoke(code, isStatic ? Opcodes.InvokeStatic : Opcodes.InvokeSpecial, bodyReference);
        stack = Math.Max(stack, method.ReturnSlots);

        int locals = temporary;

        if (afterReference is int after)
        {
            if (method.ReturnSlots > 0)
                Store(code, method.ReturnType, temporary);

            LoadReceiver(code, isStatic);

            if (includeArguments)
                LoadArguments(code, method, receiverSlots);

            if (method.ReturnSlots > 0)
                Load(code, method.ReturnType, temporary);

            Invoke(code, Opcodes.InvokeStatic, after);
            stack = Math.Max(stack, 1 + (includeArguments ? method.ParameterSlots : 0) + method.ReturnSlots);
            locals += method.ReturnSlots;
        }

        code.Add(ReturnOpcode(method.ReturnType));

        return new StubCode(stack, locals, [.. code]);
    }

    /// <summary>Encodes a <c>Code</c> attribute around the stub, with no exception table and no nested attributes.</summary>
    /// <param name="nameIndex">The constant pool index of the Utf8 constant <c>Code</c>.</param>
    /// <param name="stub">The stub.</param>
    /// <returns>The attribute, starting with its name index.</returns>
    internal static byte[] CodeAttribute(int nameIndex, StubCode stub)
    {
        int length = 12 + stub.Code.Length;
        List<byte> attribute = [];

        AddUInt16(attribute, nameIndex);
        AddUInt32(attribute, length);
        AddUInt16(attribute, stub.MaxStack);
        AddUInt16(attribute, stub.MaxLocals);
        AddUInt32(attribute, stub.Code.Length);
        attribute.AddRange(stub.Code);
        AddUInt16(attribute, value: 0);
        AddUInt16(attribute, value: 0);

        return [.. attribute];
    }

    private static void AddUInt16(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void AddUInt32(List<byte> bytes, int value)
    {
        AddUInt16(bytes, value >> 16);
        AddUInt16(bytes, value & 0xffff);
    }

    private static void Emit(List<byte> code, byte shortForm, byte longForm, int slot)
    {
        if (slot > byte.MaxValue)
            throw new InvalidOperationException($"Local variable slot {slot} needs the wide prefix, which advice does not use.");

        if (slot <= 3)
            code.Add((byte)(shortForm + slot));
        else
        {
            code.Add(longForm);
            code.Add((byte)slot);
        }
    }

    private static void Invoke(List<byte> code, byte opcode, int reference)
    {
        code.Add(opcode);
        AddUInt16(code, reference);
    }

    private static void Load(List<byte> code, string type, int slot)
    {
        switch (type[0])
        {
            case 'J':
                Emit(code, shortForm: 0x1e, longForm: 0x16, slot);

                break;
            case 'F':
                Emit(code, shortForm: 0x22, longForm: 0x17, slot);

                break;
            case 'D':
                Emit(code, shortForm: 0x26, longForm: 0x18, slot);

                break;
            case 'L' or '[':
                Emit(code, shortForm: 0x2a, longForm: 0x19, slot);

                break;
            default:
                Emit(code, shortForm: 0x1a, longForm: 0x15, slot);

                break;
        }
    }

    private static void LoadArguments(List<byte> code, MethodDescriptor method, int firstSlot)
    {
        int slot = firstSlot;

        foreach (string parameter in method.Parameters)
        {
            Load(code, parameter, slot);
            slot += MethodDescriptor.Slots(parameter);
        }
    }

    private static void LoadReceiver(List<byte> code, bool isStatic)
    {
        if (isStatic)
            code.Add(AconstNull);
        else
            Emit(code, shortForm: 0x2a, longForm: 0x19, slot: 0);
    }

    private static byte ReturnOpcode(string type)
    {
        return type[0] switch
        {
            'V' => ReturnVoid,
            'J' => 0xad,
            'F' => 0xae,
            'D' => 0xaf,
            'L' or '[' => 0xb0,
            _ => 0xac,
        };
    }

    private static void Store(List<byte> code, string type, int slot)
    {
        switch (type[0])
        {
            case 'J':
                Emit(code, shortForm: 0x3f, longForm: 0x37, slot);

                break;
            case 'F':
                Emit(code, shortForm: 0x43, longForm: 0x38, slot);

                break;
            case 'D':
                Emit(code, shortForm: 0x47, longForm: 0x39, slot);

                break;
            case 'L' or '[':
                Emit(code, shortForm: 0x4b, longForm: 0x3a, slot);

                break;
            default:
                Emit(code, shortForm: 0x3b, longForm: 0x36, slot);

                break;
        }
    }
}

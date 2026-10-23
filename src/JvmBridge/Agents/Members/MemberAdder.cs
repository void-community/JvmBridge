using JvmBridge.ClassFiles;
using JvmBridge.ClassFiles.Advice;
using JvmBridge.ClassFiles.Code;

namespace JvmBridge.Agents;

/// <summary>
/// Adds the getters, setters and invokers of <see cref="ClassPatch.AddFieldGetter"/>, <see cref="ClassPatch.AddFieldSetter"/> and <see cref="ClassPatch.AddInvoker"/>: public synthetic methods of straight-line code, static for a static member, which give
/// code in another class a way to a member that stays private.
/// </summary>
internal static class MemberAdder
{
    private const int FinalFlag = 0x0010;
    private const int InterfaceFlag = 0x0200;
    private const int PublicSynthetic = 0x1001;
    private const int StaticFlag = 0x0008;

    /// <summary>Gets the descriptor that the added method has in a class, or why it cannot be added there.</summary>
    internal static (string? Descriptor, AdviceSkip? Skip) Describe(JavaClassFile model, MemberAddition addition)
    {
        if ((model.AccessFlags & InterfaceFlag) != 0)
            return (null, new AdviceSkip(PatchSkipCategory.InterfaceClass, $"{model.Name} is an interface, to which members are not added"));

        if (addition.Kind == MemberAddition.Invoker)
        {
            string descriptor = addition.TargetDescriptor ?? string.Empty;

            return model.FindMethod(addition.Target, descriptor) is null
                ? (null, new AdviceSkip(PatchSkipCategory.MethodNotFound, $"method {addition.Target}{descriptor} not found"))
                : (descriptor, null);
        }

        if (FindField(model, addition) is not { } field)
            return (null, new AdviceSkip(PatchSkipCategory.MethodNotFound, $"field {addition.Target}{(addition.TargetDescriptor is null ? string.Empty : " " + addition.TargetDescriptor)} not found"));

        string type = addition.Type ?? field.Descriptor;

        if (type != field.Descriptor && (IsPrimitive(type) || IsPrimitive(field.Descriptor)))
            return (null, new AdviceSkip(PatchSkipCategory.HookShapeMismatch, $"field {field.Name} is {field.Descriptor}, which a {(addition.Kind == MemberAddition.Getter ? "getter" : "setter")} of {type} cannot convert"));

        if (addition.Kind == MemberAddition.Setter && (field.AccessFlags & FinalFlag) != 0)
            return (null, new AdviceSkip(PatchSkipCategory.HookShapeMismatch, $"field {field.Name} is final, so a setter could not write it"));

        return (addition.Kind == MemberAddition.Getter ? "()" + type : "(" + type + ")V", null);
    }

    /// <summary>Adds the method, or says why it cannot be added.</summary>
    internal static AdviceSkip? Add(ClassFileEditor editor, MemberAddition addition, string descriptor)
    {
        JavaClassFile model = editor.Model;
        string owner = model.Name;

        if (model.FindMethod(addition.Name, descriptor) is not null)
            return new AdviceSkip(PatchSkipCategory.AlreadyAdvised, $"{owner} declares {addition.Name}{descriptor} already");

        if (addition.Kind == MemberAddition.Invoker)
        {
            ClassMember method = model.FindMethod(addition.Target, descriptor) ?? throw new InvalidOperationException(message: "The method disappeared.");
            bool isStatic = (method.AccessFlags & StaticFlag) != 0;
            MethodDescriptor parsed = MethodDescriptor.Parse(descriptor);
            int receiver = isStatic ? 0 : 1;

            StubCode invoker = editor.Build(
                receiver + parsed.ParameterSlots,
                code => Return(
                    isStatic
                        ? LoadArguments(code, parsed, firstSlot: 0).Invoke(JavaMethod.Static(owner, addition.Target, descriptor))
                        : LoadArguments(code.ALoad(slot: 0), parsed, firstSlot: 1).InvokeSpecial(owner, addition.Target, descriptor),
                    parsed.ReturnType
                )
            );

            editor.AddMethod(PublicSynthetic | (isStatic ? StaticFlag : 0), addition.Name, descriptor, invoker);

            return null;
        }

        ClassMember field = FindField(model, addition) ?? throw new InvalidOperationException(message: "The field disappeared.");
        bool staticField = (field.AccessFlags & StaticFlag) != 0;
        int self = staticField ? 0 : 1;
        string type = addition.Type ?? field.Descriptor;

        StubCode body = addition.Kind == MemberAddition.Getter
            ? editor.Build(self, code => Return(staticField ? code.GetStatic(owner, field.Name, field.Descriptor) : code.ALoad(slot: 0).GetField(owner, field.Name, field.Descriptor), type))
            : editor.Build(self + Slots(type), code => Store(staticField ? code : code.ALoad(slot: 0), owner, field, type, self).Return());

        editor.AddMethod(PublicSynthetic | (staticField ? StaticFlag : 0), addition.Name, descriptor, body);

        return null;
    }

    private static ClassMember? FindField(JavaClassFile model, MemberAddition addition) => model.Fields.FirstOrDefault(field => field.Name == addition.Target && (addition.TargetDescriptor is null || field.Descriptor == addition.TargetDescriptor));

    private static bool IsPrimitive(string type) => type[0] is not ('L' or '[');

    private static CodeBuilder Load(CodeBuilder code, string type, int slot) => type[0] switch
    {
        'J' => code.LLoad(slot),
        'F' => code.FLoad(slot),
        'D' => code.DLoad(slot),
        'L' or '[' => code.ALoad(slot),
        _ => code.ILoad(slot),
    };

    private static CodeBuilder LoadArguments(CodeBuilder code, MethodDescriptor descriptor, int firstSlot)
    {
        int slot = firstSlot;

        foreach (string parameter in descriptor.Parameters)
        {
            code = Load(code, parameter, slot);
            slot += Slots(parameter);
        }

        return code;
    }

    private static CodeBuilder Return(CodeBuilder code, string type) => type[0] switch
    {
        'V' => code.Return(),
        'J' => code.LReturn(),
        'F' => code.FReturn(),
        'D' => code.DReturn(),
        'L' or '[' => code.AReturn(),
        _ => code.IReturn(),
    };

    private static int Slots(string type) => type[0] is 'J' or 'D' ? 2 : 1;

    // The value is in the first slot after the receiver; a reference of a wider type is cast to the field's type first.
    private static CodeBuilder Store(CodeBuilder code, string owner, ClassMember field, string type, int slot)
    {
        CodeBuilder loaded = Load(code, type, slot);

        if (type != field.Descriptor)
            loaded = loaded.CheckCast(field.Descriptor[0] == 'L' ? field.Descriptor[1..^1] : field.Descriptor);

        return (field.AccessFlags & StaticFlag) != 0 ? loaded.PutStatic(owner, field.Name, field.Descriptor) : loaded.PutField(owner, field.Name, field.Descriptor);
    }
}

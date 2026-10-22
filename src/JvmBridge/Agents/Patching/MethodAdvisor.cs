using JvmBridge.ClassFiles;
using JvmBridge.ClassFiles.Advice;

namespace JvmBridge.Agents;

/// <summary>
/// Puts advice around a method: the code of the method moves, byte for byte, into a private synthetic method named <c>name$jvmbridge$body</c>, and the method itself becomes a short stub that calls the before hook, the body and the after hook. The method keeps its
/// flags, signature, annotations and exception list, so reflection and callers see the same method, and the original code keeps its stack map frames and line numbers.
/// </summary>
internal static class MethodAdvisor
{
    private const int BridgeFlag = 0x0040;
    private const int InterfaceFlag = 0x0200;
    private const int StaticFlag = 0x0008;
    private const int SyntheticFlag = 0x1000;
    private const int MaximumSlots = 255;

    /// <summary>Gets the name of the method that holds the original code of an advised method.</summary>
    /// <param name="name">The name of the advised method.</param>
    /// <returns>The name of the body method.</returns>
    internal static string BodyName(string name) => name + "$jvmbridge$body";

    /// <summary>Gets the key that identifies a method in the record of what was advised.</summary>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor.</param>
    /// <returns>The key.</returns>
    internal static string Key(string name, string descriptor) => name + descriptor;

    /// <summary>Puts advice around a method.</summary>
    /// <param name="editor">The editor of the class.</param>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor.</param>
    /// <param name="hooks">The hooks.</param>
    /// <returns><see langword="null"/> when the method was advised, otherwise why not.</returns>
    internal static AdviceSkip? Advise(ClassFileEditor editor, string name, string descriptor, AdviceHooks hooks)
    {
        AdviceSkip? unsuitable = CheckMethod(editor.Model, name, descriptor);

        if (unsuitable is not null)
            return unsuitable;

        MethodDescriptor parsed = MethodDescriptor.Parse(descriptor);
        string owner = editor.Model.Name;

        if (hooks.Before is { } before && CheckHook(before, isAfter: false, hooks.IncludeArguments, owner, parsed) is { } beforeProblem)
            return new AdviceSkip(PatchSkipCategory.HookShapeMismatch, beforeProblem);

        if (hooks.After is { } after && CheckHook(after, isAfter: true, hooks.IncludeArguments, owner, parsed) is { } afterProblem)
            return new AdviceSkip(PatchSkipCategory.HookShapeMismatch, afterProblem);

        bool isStatic = (editor.Model.FindMethod(name, descriptor)?.AccessFlags & StaticFlag) != 0;
        int locals = (isStatic ? 0 : 1) + parsed.ParameterSlots + (hooks.After is null ? 0 : parsed.ReturnSlots);

        if (locals > MaximumSlots)
            return new AdviceSkip(PatchSkipCategory.TooManySlots, $"{name}{descriptor} has {locals} local variable slots, more than the {MaximumSlots} that advice supports");

        int bodyReference = editor.AddMethodReference(owner, BodyName(name), descriptor);
        int? beforeReference = hooks.Before is { } beforeHook ? editor.AddMethodReference(beforeHook.Owner, beforeHook.Name, beforeHook.Descriptor) : null;
        int? afterReference = hooks.After is { } afterHook ? editor.AddMethodReference(afterHook.Owner, afterHook.Name, afterHook.Descriptor) : null;

        editor.ReplaceBodyWithStub(name, descriptor, BodyName(name), AdviceStub.Build(isStatic, parsed, bodyReference, beforeReference, afterReference, hooks.IncludeArguments));

        return null;
    }

    /// <summary>
    /// Gives a method the body and the stub of advice, without hooks. A class that was advised when it was defined must keep its body methods for as long as it lives, so when its advice is removed the stub only calls the body.
    /// </summary>
    /// <param name="editor">The editor of the class, before any advice.</param>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor.</param>
    /// <returns><see langword="null"/> when the stub was written, otherwise why not.</returns>
    internal static AdviceSkip? Neutralize(ClassFileEditor editor, string name, string descriptor)
    {
        return Advise(editor, name, descriptor, new AdviceHooks(Before: null, After: null, IncludeArguments: false));
    }

    private static AdviceSkip? CheckMethod(JavaClassFile model, string name, string descriptor)
    {
        ClassMember? method = model.FindMethod(name, descriptor);

        if (method is null)
            return new AdviceSkip(PatchSkipCategory.MethodNotFound, $"method {name}{descriptor} not found");

        if (name is "<init>" or "<clinit>")
            return new AdviceSkip(PatchSkipCategory.InitializerNotSupported, $"{name} is a constructor or initializer, which advice does not support");

        if ((model.AccessFlags & InterfaceFlag) != 0)
            return new AdviceSkip(PatchSkipCategory.InterfaceClass, $"{model.Name} is an interface, whose methods advice does not support");

        if (method.Code is null)
            return new AdviceSkip(PatchSkipCategory.NativeOrAbstractMethod, $"{name}{descriptor} has no code to advise: it is native or abstract");

        if ((method.AccessFlags & (BridgeFlag | SyntheticFlag)) != 0)
            return new AdviceSkip(PatchSkipCategory.BridgeOrSyntheticMethod, $"{name}{descriptor} is a bridge or synthetic method");

        return model.FindMethod(BodyName(name), descriptor) is null
            ? null
            : new AdviceSkip(PatchSkipCategory.AlreadyAdvised, $"{name}{descriptor} is advised already");
    }

    private static string? CheckHook(JavaMethod hook, bool isAfter, bool includeArguments, string owner, MethodDescriptor target)
    {
        if (hook.Kind != JavaMethodKind.Static)
            return $"the hook {hook} must be static; describe it with JavaMethod.Static";

        string returnType = isAfter ? target.ReturnType : "V";
        string result = isAfter && target.ReturnSlots > 0 ? target.ReturnType : string.Empty;
        string arguments = isAfter && !includeArguments ? string.Empty : target.ParameterText;
        string withObject = $"(Ljava/lang/Object;{arguments}{result}){returnType}";
        string withOwner = $"(L{owner};{arguments}{result}){returnType}";

        return hook.Descriptor == withObject || hook.Descriptor == withOwner
            ? null
            : $"the hook {hook} must have the descriptor {withObject}, or {withOwner}";
    }
}

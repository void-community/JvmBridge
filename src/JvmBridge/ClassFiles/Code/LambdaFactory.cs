using JvmBridge.ClassFiles.Advice;

namespace JvmBridge.ClassFiles.Code;

/// <summary>
/// Adds what an <c>invokedynamic</c> through <c>LambdaMetafactory.metafactory</c> needs, as javac writes it for a lambda or a method reference: the bootstrap method handle, the
/// erased and the instantiated type of the functional interface's method, the handle of the implementation, a <c>BootstrapMethods</c> entry, the call site and the
/// <c>InnerClasses</c> entry of <c>MethodHandles$Lookup</c>. Constants and entries that exist are reused.
/// </summary>
internal static class LambdaFactory
{
    internal const int MinimumMajorVersion = 52;

    private const string Metafactory = "java/lang/invoke/LambdaMetafactory";

    private const string MetafactoryDescriptor =
        "(Ljava/lang/invoke/MethodHandles$Lookup;Ljava/lang/String;Ljava/lang/invoke/MethodType;Ljava/lang/invoke/MethodType;Ljava/lang/invoke/MethodHandle;Ljava/lang/invoke/MethodType;)Ljava/lang/invoke/CallSite;";

    // From class file version 59 (Java 15), javac names a private method in a lambda handle with REF_invokeVirtual, or REF_invokeInterface in an interface, as nestmates allow;
    // before that, with REF_invokeSpecial.
    private const int NestmateHandleVersion = 59;

    private const int InvokeVirtual = 5;
    private const int InvokeStatic = 6;
    private const int InvokeSpecial = 7;
    private const int InvokeInterface = 9;

    /// <summary>Adds the constants of a lambda call site.</summary>
    /// <param name="pool">The pool of the class the call site is in.</param>
    /// <param name="functionalMethod">The single abstract method of the functional interface, with its erased descriptor.</param>
    /// <param name="implementation">The method the lambda calls.</param>
    /// <returns>The index of the <c>InvokeDynamic</c> constant, and the stack slots of the values the call site captures.</returns>
    /// <exception cref="ArgumentException">The method kinds or descriptors do not fit together.</exception>
    /// <exception cref="InvalidOperationException">The class file is older than version 52.</exception>
    internal static (int Index, int CapturedSlots) Add(IConstantPool pool, JavaMethod functionalMethod, JavaMethod implementation)
    {
        if (functionalMethod.Kind != JavaMethodKind.Interface)
            throw new ArgumentException($"{functionalMethod} must be the abstract method of a functional interface; describe it with JavaMethod.Interface.", nameof(functionalMethod));

        if (pool.MajorVersion < MinimumMajorVersion)
            throw new InvalidOperationException($"A lambda needs LambdaMetafactory, which Java 8 introduced, so the class file must be of version {MinimumMajorVersion} or later, not {pool.MajorVersion}.");

        MethodDescriptor abstractMethod = Parse(functionalMethod, nameof(functionalMethod));
        MethodDescriptor target = Parse(implementation, nameof(implementation));
        List<string> sources = implementation.Kind == JavaMethodKind.Static ? [.. target.Parameters] : [Reference(implementation.Owner), .. target.Parameters];
        int captured = sources.Count - abstractMethod.Parameters.Count;

        if (captured < 0)
        {
            throw new ArgumentException(
                $"{implementation} takes {sources.Count} values with its receiver, fewer than the {abstractMethod.Parameters.Count} parameters of {functionalMethod}.",
                nameof(implementation)
            );
        }

        string[] capturedTypes = [.. sources.Take(captured)];
        string instantiated = Instantiate(abstractMethod, sources.Skip(captured).ToArray(), target.ReturnType, functionalMethod, implementation);
        (int kind, ConstantTag tag) = Handle(pool, implementation);

        // In javac's order: the bootstrap method, then its static arguments, then the call site.
        int bootstrapHandle = pool.AddMethodHandle(InvokeStatic, ConstantTag.Methodref, Metafactory, name: "metafactory", MetafactoryDescriptor);
        int erasedType = pool.AddMethodType(functionalMethod.Descriptor);
        int implementationHandle = pool.AddMethodHandle(kind, tag, implementation.Owner, implementation.Name, implementation.Descriptor);
        int instantiatedType = pool.AddMethodType(instantiated);
        int bootstrapMethod = pool.AddBootstrapMethod(bootstrapHandle, [erasedType, implementationHandle, instantiatedType]);
        int callSite = pool.AddInvokeDynamic(bootstrapMethod, functionalMethod.Name, $"({string.Concat(capturedTypes)}){Reference(functionalMethod.Owner)}");

        pool.AddInnerClass(innerClass: "java/lang/invoke/MethodHandles$Lookup", outerClass: "java/lang/invoke/MethodHandles", simpleName: "Lookup", accessFlags: 0x0019);

        return (callSite, capturedTypes.Sum(MethodDescriptor.Slots));
    }

    private static (int Kind, ConstantTag Tag) Handle(IConstantPool pool, JavaMethod implementation)
    {
        bool inInterface = implementation.Kind == JavaMethodKind.Interface || pool.IsInterface(implementation.Owner);
        ConstantTag tag = inInterface ? ConstantTag.InterfaceMethodref : ConstantTag.Methodref;

        if (implementation.Kind == JavaMethodKind.Static)
            return (InvokeStatic, tag);

        bool special = pool.MajorVersion < NestmateHandleVersion && pool.IsPrivateMethod(implementation.Owner, implementation.Name, implementation.Descriptor);

        if (special)
            return (InvokeSpecial, tag);

        return (inInterface ? InvokeInterface : InvokeVirtual, tag);
    }

    // The instantiated method type is the functional method's type as the implementation narrows it, as javac writes it for a generic interface: a reference parameter or result takes the
    // implementation's reference type, or the box of its primitive, and a primitive stays the functional method's own.
    private static string Instantiate(MethodDescriptor abstractMethod, string[] parameters, string result, JavaMethod functionalMethod, JavaMethod implementation)
    {
        string[] instantiated = new string[parameters.Length];

        for (int index = 0; index < parameters.Length; index++)
            instantiated[index] = IsReference(abstractMethod.Parameters[index]) ? Boxed(parameters[index]) : abstractMethod.Parameters[index];

        string returned = abstractMethod.ReturnType;

        if (IsReference(returned))
        {
            returned = result == "V"
                ? throw new ArgumentException($"{implementation} returns nothing, but {functionalMethod} returns {abstractMethod.ReturnType}.", nameof(implementation))
                : Boxed(result);
        }

        return $"({string.Concat(instantiated)}){returned}";
    }

    private static string Boxed(string type) => type switch
    {
        "Z" => "Ljava/lang/Boolean;",
        "B" => "Ljava/lang/Byte;",
        "C" => "Ljava/lang/Character;",
        "S" => "Ljava/lang/Short;",
        "I" => "Ljava/lang/Integer;",
        "J" => "Ljava/lang/Long;",
        "F" => "Ljava/lang/Float;",
        "D" => "Ljava/lang/Double;",
        _ => type,
    };

    private static bool IsReference(string type) => type[0] is 'L' or '[';

    private static MethodDescriptor Parse(JavaMethod method, string parameter)
    {
        try { return MethodDescriptor.Parse(method.Descriptor); }
        catch (ClassFormatException exception) { throw new ArgumentException(exception.Message, parameter, exception); }
    }

    private static string Reference(string className) => className.StartsWith('[') ? className : $"L{className};";
}

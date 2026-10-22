namespace JvmBridge.ClassFiles;

/// <summary>How a method is invoked, which selects the invoke instruction it matches.</summary>
public enum JavaMethodKind
{
    /// <summary>An <c>invokestatic</c> call.</summary>
    Static,

    /// <summary>An <c>invokevirtual</c> call.</summary>
    Virtual,

    /// <summary>An <c>invokeinterface</c> call.</summary>
    Interface,
}

/// <summary>Identifies an invoked method by invocation kind, owner, name and descriptor.</summary>
/// <param name="Kind">How the method is invoked.</param>
/// <param name="Owner">The internal name of the class the call site references.</param>
/// <param name="Name">The method name.</param>
/// <param name="Descriptor">The method descriptor.</param>
public readonly record struct JavaMethod(JavaMethodKind Kind, string Owner, string Name, string Descriptor)
{
    /// <summary>Identifies a method called with <c>invokestatic</c>.</summary>
    /// <param name="owner">The internal name of the class the call site references.</param>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor.</param>
    /// <returns>The method identity.</returns>
    public static JavaMethod Static(string owner, string name, string descriptor) => new(JavaMethodKind.Static, owner, name, descriptor);

    /// <summary>Identifies a method called with <c>invokevirtual</c>.</summary>
    /// <param name="owner">The internal name of the class the call site references.</param>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor.</param>
    /// <returns>The method identity.</returns>
    public static JavaMethod Virtual(string owner, string name, string descriptor) => new(JavaMethodKind.Virtual, owner, name, descriptor);

    /// <summary>Identifies a method called with <c>invokeinterface</c>.</summary>
    /// <param name="owner">The internal name of the interface the call site references.</param>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor.</param>
    /// <returns>The method identity.</returns>
    public static JavaMethod Interface(string owner, string name, string descriptor) => new(JavaMethodKind.Interface, owner, name, descriptor);

    internal byte Opcode => Kind switch
    {
        JavaMethodKind.Static => Opcodes.InvokeStatic,
        JavaMethodKind.Virtual => Opcodes.InvokeVirtual,
        _ => Opcodes.InvokeInterface,
    };

    /// <inheritdoc/>
    public override string ToString() => $"{Owner}.{Name}{Descriptor}";
}

namespace JvmBridge.Agents;

/// <summary>The class, name and descriptor of a method, resolved from a raw method ID.</summary>
/// <param name="ClassName">The internal name of the declaring class, for example <c>java/lang/String</c>.</param>
/// <param name="Name">The name the method is declared with, for example <c>toString</c>.</param>
/// <param name="Descriptor">The method descriptor, for example <c>(I)Ljava/lang/String;</c>.</param>
public readonly record struct JavaMethodName(string ClassName, string Name, string Descriptor)
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return ClassName + "." + Name + Descriptor;
    }
}

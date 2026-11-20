namespace JvmBridge.Generator.Proxies;

/// <summary>A public method or constructor of a Java class that a typed proxy exposes.</summary>
/// <remarks>Initializes a new instance of the <see cref="JavaProxyMember"/> class, which describes a method or constructor for the proxy generator to wrap.</remarks>
/// <param name="name">The Java name, or <c>&lt;init&gt;</c> for a constructor.</param>
/// <param name="descriptor">The JNI descriptor, for example <c>(ILjava/lang/String;)V</c>.</param>
/// <param name="isStatic">Whether the method is static.</param>
public sealed class JavaProxyMember(string name, string descriptor, bool isStatic)
{
    /// <summary>Gets the JNI descriptor.</summary>
    /// <value>The parameter types in parentheses followed by the return type, for example <c>(ILjava/lang/String;)V</c>.</value>
    public string Descriptor { get; } = descriptor;

    /// <summary>Gets whether the member is a constructor.</summary>
    /// <value><see langword="true"/> if the name is <c>&lt;init&gt;</c>; otherwise, <see langword="false"/>.</value>
    public bool IsConstructor => Name == "<init>";

    /// <summary>Gets whether the method is static.</summary>
    /// <value><see langword="true"/> if the proxy calls the method on the class and not on an instance; otherwise, <see langword="false"/>.</value>
    public bool IsStatic { get; } = isStatic;

    /// <summary>Gets the Java name.</summary>
    /// <value>The name as the class file spells it, or <c>&lt;init&gt;</c> for a constructor.</value>
    public string Name { get; } = name;
}

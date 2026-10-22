namespace JvmBridge.Generator.Proxies;

/// <summary>A public method or constructor of a Java class that a typed proxy exposes.</summary>
public sealed class JavaProxyMember
{
    /// <summary>Initializes a member.</summary>
    /// <param name="name">The Java name, or <c>&lt;init&gt;</c> for a constructor.</param>
    /// <param name="descriptor">The JNI descriptor, for example <c>(ILjava/lang/String;)V</c>.</param>
    /// <param name="isStatic">Whether the method is static.</param>
    public JavaProxyMember(string name, string descriptor, bool isStatic)
    {
        Name = name;
        Descriptor = descriptor;
        IsStatic = isStatic;
    }

    /// <summary>Gets the JNI descriptor.</summary>
    public string Descriptor { get; }

    /// <summary>Gets whether the member is a constructor.</summary>
    public bool IsConstructor => Name == "<init>";

    /// <summary>Gets whether the method is static.</summary>
    public bool IsStatic { get; }

    /// <summary>Gets the Java name.</summary>
    public string Name { get; }
}

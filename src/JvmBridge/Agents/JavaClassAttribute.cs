namespace JvmBridge.Agents;

/// <summary>Marks a <c>static partial</c> class whose <see cref="JavaMethodAttribute"/> methods become <c>native</c> methods of a generated Java class. The generator adds a <c>Definition</c> property to define it with <see cref="AgentContext.DefineClass"/>.</summary>
/// <param name="name">The internal name of the Java class, for example <c>pkg/Callbacks</c>.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class JavaClassAttribute(string name) : Attribute
{
    /// <summary>Gets the internal name of the Java class.</summary>
    public string Name { get; } = name;
}

/// <summary>
/// Marks a static method of a <see cref="JavaClassAttribute"/> class as the implementation of a <c>public static native</c> Java method. An optional first parameter of type <c>JavaEnvironment</c> receives the callback's JNI environment.
/// The other parameters and the return type may be <c>bool</c>, <c>sbyte</c>, <c>char</c>, <c>short</c>, <c>int</c>, <c>long</c>, <c>float</c>, <c>double</c>, <c>string</c> or <c>nint</c> (a Java object, typed by <see cref="JavaTypeAttribute"/>).
/// The Java method is named like the C# method with a lower-case first letter unless a name is given. An exception the method throws is reported and contained, and the JVM receives a default value.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class JavaMethodAttribute : Attribute
{
    /// <summary>Initializes the attribute with the method's own name as the Java name.</summary>
    public JavaMethodAttribute() { }

    /// <summary>Initializes the attribute with a Java method name.</summary>
    /// <param name="name">The Java method name.</param>
    public JavaMethodAttribute(string name) => Name = name;

    /// <summary>Gets the Java method name, or <see langword="null"/> to use the C# method name with its first letter in lower case.</summary>
    public string? Name { get; }
}

/// <summary>Gives the Java type of a <c>nint</c> object parameter or return value; without it the type is <c>java/lang/Object</c>.</summary>
/// <param name="descriptor">A field descriptor, for example <c>Ljava/util/List;</c>.</param>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false, Inherited = false)]
public sealed class JavaTypeAttribute(string descriptor) : Attribute
{
    /// <summary>Gets the field descriptor.</summary>
    public string Descriptor { get; } = descriptor;
}

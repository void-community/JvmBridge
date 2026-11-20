using System.Collections.Generic;

namespace JvmBridge.Generator.Proxies;

/// <summary>A Java class that a typed proxy wraps, with the public members to expose.</summary>
/// <remarks>Initializes a new instance of the <see cref="JavaProxyClass"/> class, which describes a Java class for the proxy generator to wrap.</remarks>
/// <param name="name">The internal name, for example <c>com/acme/Service</c>.</param>
/// <param name="members">The public methods and constructors.</param>
public sealed class JavaProxyClass(string name, IReadOnlyList<JavaProxyMember> members)
{
    /// <summary>Gets the public methods and constructors.</summary>
    /// <value>The members as they were given to the constructor; a proxy method is generated for each.</value>
    public IReadOnlyList<JavaProxyMember> Members { get; } = members;

    /// <summary>Gets the internal class name.</summary>
    /// <value>The name with slashes between its packages, for example <c>com/acme/Service</c>.</value>
    public string Name { get; } = name;
}

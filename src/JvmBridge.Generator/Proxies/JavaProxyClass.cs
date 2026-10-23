using System.Collections.Generic;

namespace JvmBridge.Generator.Proxies;

/// <summary>A Java class that a typed proxy wraps, with the public members to expose.</summary>
public sealed class JavaProxyClass
{
    /// <summary>Describes a Java class for the proxy generator to wrap.</summary>
    /// <param name="name">The internal name, for example <c>com/acme/Service</c>.</param>
    /// <param name="members">The public methods and constructors.</param>
    public JavaProxyClass(string name, IReadOnlyList<JavaProxyMember> members)
    {
        Name = name;
        Members = members;
    }

    /// <summary>Gets the public methods and constructors.</summary>
    public IReadOnlyList<JavaProxyMember> Members { get; }

    /// <summary>Gets the internal class name.</summary>
    public string Name { get; }
}

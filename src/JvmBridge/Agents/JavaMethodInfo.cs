namespace JvmBridge.Agents;

/// <summary>Copied declared-method metadata. Id is valid only while its declaring class remains loaded; it does not retain the class or its loader.</summary>
public sealed record JavaMethodInfo(nint Id, string Name, string Descriptor, string? GenericSignature, int Modifiers);

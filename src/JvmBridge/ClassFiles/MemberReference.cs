namespace JvmBridge.ClassFiles;

/// <summary>A resolved field or method reference constant.</summary>
/// <param name="Kind">Fieldref, Methodref or InterfaceMethodref.</param>
/// <param name="Owner">The internal name of the referenced class, for example <c>java/util/Collections</c>.</param>
/// <param name="Name">The member name.</param>
/// <param name="Descriptor">The member descriptor.</param>
public readonly record struct MemberReference(ConstantTag Kind, string Owner, string Name, string Descriptor);

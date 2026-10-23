namespace JvmBridge.Agents;

/// <summary>A method that a patch adds to a class to reach one of its members: a getter or setter of a field, or an invoker of a method.</summary>
/// <param name="Kind">0 for a getter, 1 for a setter, 2 for an invoker.</param>
/// <param name="Target">The name of the field or method it reaches.</param>
/// <param name="TargetDescriptor">The field's type or the method's descriptor, or <see langword="null"/> to take the field's type from the class.</param>
/// <param name="Name">The name of the added method.</param>
/// <param name="Type">The type the getter returns or the setter takes, or <see langword="null"/> for the field's own type.</param>
internal sealed record MemberAddition(int Kind, string Target, string? TargetDescriptor, string Name, string? Type)
{
    internal const int Getter = 0;

    internal const int Invoker = 2;

    internal const int Setter = 1;

    internal string Describe() => Kind switch
    {
        Getter => $"getter of field {Target}",
        Setter => $"setter of field {Target}",
        _ => $"invoker of {Target}{TargetDescriptor}",
    };
}

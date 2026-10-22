using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Testing;

/// <summary>Assertions on class-file bytes, such as the output of a <c>ClassPatch</c>, that need no JVM.</summary>
public static class ClassFileAssert
{
    /// <summary>Asserts that a method contains a call of another method.</summary>
    /// <param name="classBytes">The class-file bytes.</param>
    /// <param name="method">The name of the method that should contain the call.</param>
    /// <param name="owner">The internal name of the class the call references, for example <c>java/util/Collections</c>.</param>
    /// <param name="name">The called method's name.</param>
    /// <param name="descriptor">The called method's descriptor, or <see langword="null"/> to accept any. When given, the call's whole member reference must match.</param>
    public static void Calls(byte[] classBytes, string method, string owner, string name, string? descriptor = null)
    {
        MemberReference[] calls = CallsIn(classBytes, method);

        if (!calls.Any(call => Matches(call, owner, name, descriptor)))
            Assert.Fail($"{method} does not call {owner}.{name}{descriptor}. It calls: {Describe(calls)}.");
    }

    /// <summary>Asserts that a method does not contain a call of another method.</summary>
    /// <param name="classBytes">The class-file bytes.</param>
    /// <param name="method">The name of the method that should not contain the call.</param>
    /// <param name="owner">The internal name of the class the call references.</param>
    /// <param name="name">The called method's name.</param>
    /// <param name="descriptor">The called method's descriptor, or <see langword="null"/> to reject any. When given, only a call with this exact descriptor fails the assertion.</param>
    public static void DoesNotCall(byte[] classBytes, string method, string owner, string name, string? descriptor = null)
    {
        if (CallsIn(classBytes, method).Any(call => Matches(call, owner, name, descriptor)))
            Assert.Fail($"{method} still calls {owner}.{name}{descriptor}.");
    }

    private static MemberReference[] CallsIn(byte[] classBytes, string method)
    {
        ArgumentNullException.ThrowIfNull(classBytes);
        JavaClassFile file = JavaClassFile.Read(classBytes);
        ClassMember member = file.FindMethod(method) ?? throw new Xunit.Sdk.XunitException($"The class {file.Name} has no method {method}.");
        CodeAttribute code = member.Code ?? throw new Xunit.Sdk.XunitException($"{method} has no code.");

        return
        [
            .. code.Instructions
                .Where(instruction => instruction.ConstantIndex is not null && instruction.Opcode is >= 0xb6 and <= 0xb9)
                .Select(instruction => file.GetMemberReference(instruction.ConstantIndex ?? 0))
        ];
    }

    private static string Describe(MemberReference[] calls)
    {
        return calls.Length == 0 ? "nothing" : string.Join(separator: ", ", calls.Select(static call => call.Owner + "." + call.Name + call.Descriptor));
    }

    private static bool Matches(MemberReference call, string owner, string name, string? descriptor)
    {
        return call.Owner == owner && call.Name == name && (descriptor is null || call.Descriptor == descriptor);
    }
}

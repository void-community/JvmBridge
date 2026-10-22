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
    public static void Calls(byte[] classBytes, string method, string owner, string name)
    {
        string[] calls = CallsIn(classBytes, method);

        if (!calls.Contains(owner + "." + name, StringComparer.Ordinal))
            Assert.Fail($"{method} does not call {owner}.{name}. It calls: {(calls.Length == 0 ? "nothing" : string.Join(separator: ", ", calls))}.");
    }

    /// <summary>Asserts that a method does not contain a call of another method.</summary>
    /// <param name="classBytes">The class-file bytes.</param>
    /// <param name="method">The name of the method that should not contain the call.</param>
    /// <param name="owner">The internal name of the class the call references.</param>
    /// <param name="name">The called method's name.</param>
    public static void DoesNotCall(byte[] classBytes, string method, string owner, string name)
    {
        if (CallsIn(classBytes, method).Contains(owner + "." + name, StringComparer.Ordinal))
            Assert.Fail($"{method} still calls {owner}.{name}.");
    }

    private static string[] CallsIn(byte[] classBytes, string method)
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
                .Select(reference => reference.Owner + "." + reference.Name)
        ];
    }
}

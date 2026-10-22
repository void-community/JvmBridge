namespace JvmBridge.ClassFiles.Advice;

/// <summary>The code of a straight-line method and the stack and local variable sizes it needs.</summary>
/// <param name="MaxStack">The maximum operand stack depth.</param>
/// <param name="MaxLocals">The number of local variable slots.</param>
/// <param name="Code">The instructions.</param>
internal readonly record struct StubCode(int MaxStack, int MaxLocals, byte[] Code);

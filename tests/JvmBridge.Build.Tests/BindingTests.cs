using Xunit;

namespace JvmBridge.Build.Tests;

/// <summary>Ensures nonportable native signatures remain addresses while argument-array slots stay typed.</summary>
public sealed class BindingTests
{
    /// <summary>Native attributes, not method naming conventions, identify nonportable signatures.</summary>
    [Fact]
    public void RewritesOnlyVariadicSlots()
    {
        const string source = """
            public unsafe struct Functions
            {
                [NativeTypeName("int (*)(void*, ...)")]
                public delegate* unmanaged<void*, int> Call;
                [NativeTypeName("int (*)(void*, va_list)")]
                public delegate* unmanaged<void*, void*, int> CallV;
                [NativeTypeName("int (*)(void*, const jvalue*)")]
                public delegate* unmanaged<void*, void*, int> CallA;
            }
            """;

        string result = VariadicSlotRewriter.Rewrite(source);
        Assert.Contains(expectedSubstring: "public nint Call;", result, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "public nint CallV;", result, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "public delegate* unmanaged<void*, void*, int> CallA;", result, StringComparison.Ordinal);
    }
}

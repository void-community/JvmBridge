using Xunit;

namespace JvmBridge.Build.Tests;

/// <summary>Checks syntax-based ABI inspection and native layout comparisons.</summary>
public sealed class AbiTests
{
    /// <summary>Older native tables may be shorter while offsets still match.</summary>
    [Fact]
    public void AllowsOlderTablePrefixes()
    {
        NativeAbiVerifier.Verify(
            new Dictionary<string, long> { [key: "size.JNINativeInterface_"] = 16 },
            new Dictionary<string, long> { [key: "size.JNINativeInterface_"] = 24 }
        );
    }

    /// <summary>Function-pointer fields are found independently of whitespace and method bodies.</summary>
    [Fact]
    public void ParsesNativeFieldsAsCSharpSyntax()
    {
        const string source = """
            public unsafe struct Callbacks
            {
                public delegate* unmanaged<
                    void*, int> Invoke;
                public nint First, Second;
                public static nint Shared;
                public int Property => 0;
                public void Method() { string text = "}"; }
            }
            """;

        var records = NativeAbiVerifier.ParseRecords(source);
        Assert.Equal(["Invoke", "First", "Second"], [.. NativeAbiVerifier.Fields(records[key: "Callbacks"])]);
    }

    /// <summary>A managed build cannot substitute for a correct native layout.</summary>
    [Fact]
    public void RejectsWrongNativeOffset()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            static () => NativeAbiVerifier.Verify(
                new Dictionary<string, long> { [key: "offset.Callbacks.Invoke"] = 8 },
                new Dictionary<string, long> { [key: "offset.Callbacks.Invoke"] = 16 }
            )
        );

        Assert.Contains(expectedSubstring: "ABI mismatch", failure.Message, StringComparison.Ordinal);
    }
}

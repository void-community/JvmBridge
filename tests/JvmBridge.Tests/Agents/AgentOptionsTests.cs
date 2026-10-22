using JvmBridge.Agents;

using Xunit;

namespace JvmBridge.Tests;

/// <summary>Verifies how agent option text is parsed.</summary>
public sealed class AgentOptionsTests
{
    /// <summary>An entry with no key, such as a bare equals sign, is dropped rather than throwing.</summary>
    [Fact]
    public void AnEntryWithoutAKeyIsDropped()
    {
        AgentOptions options = AgentOptions.Parse(text: "=value,ok");

        Assert.Equal(expected: 1, options.Count);
        Assert.True(options.Contains(key: "ok"));
    }

    /// <summary>Missing text and empty entries parse to nothing.</summary>
    [Fact]
    public void EmptyTextHasNoEntries()
    {
        string?[] texts = [null, string.Empty, " , ,, "];

        foreach (string? text in texts)
        {
            AgentOptions options = AgentOptions.Parse(text);

            Assert.Equal(expected: 0, options.Count);
            Assert.False(options.Contains(key: "anything"));
        }
    }

    /// <summary>The original text is kept for display.</summary>
    [Fact]
    public void KeepsTheOriginalText()
    {
        Assert.Equal(expected: "a=1, b", AgentOptions.Parse(text: "a=1, b").ToString());
    }

    /// <summary>Keys ignore case, whitespace is trimmed and the last duplicate wins.</summary>
    [Fact]
    public void KeysIgnoreCaseAndTheLastDuplicateWins()
    {
        AgentOptions options = AgentOptions.Parse(text: " Mode = one , mode=two ");

        Assert.True(options.TryGetValue(key: "MODE", out string? mode));
        Assert.Equal(expected: "two", mode);
        Assert.Equal(expected: 1, options.Count);
    }

    /// <summary>Entries split on commas; the first equals sign splits a key from its value, so values may contain more.</summary>
    [Fact]
    public void ParsesKeysValuesAndFlags()
    {
        AgentOptions options = AgentOptions.Parse(text: "log=debug,trace-classes,filter=a=b");

        Assert.Equal(expected: 3, options.Count);
        Assert.True(options.TryGetValue(key: "log", out string? log));
        Assert.Equal(expected: "debug", log);
        Assert.True(options.TryGetValue(key: "trace-classes", out string? flag));
        Assert.Equal(string.Empty, flag);
        Assert.True(options.TryGetValue(key: "filter", out string? filter));
        Assert.Equal(expected: "a=b", filter);
    }

    /// <summary>A bare flag and the values true, on, yes and 1 are on; absent flags and false-like values are off.</summary>
    [Fact]
    public void ReadsFlags()
    {
        string[] enabled = ["verbose", "verbose=true", "verbose=ON", "verbose=yes", "verbose=1"];
        string[] disabled = ["verbose=false", "verbose=0", "verbose=maybe", "other", string.Empty];

        Assert.All(enabled, static text => Assert.True(AgentOptions.Parse(text).IsEnabled(key: "verbose"), text));
        Assert.All(disabled, static text => Assert.False(AgentOptions.Parse(text).IsEnabled(key: "verbose"), text));
    }

    /// <summary>Integers parse with the invariant culture, and anything else reports failure.</summary>
    [Fact]
    public void ReadsIntegers()
    {
        AgentOptions options = AgentOptions.Parse(text: "limit=-5,bad=x,empty");

        Assert.True(options.TryGetInt32(key: "limit", out int limit));
        Assert.Equal(expected: -5, limit);
        Assert.False(options.TryGetInt32(key: "bad", out int bad));
        Assert.Equal(expected: 0, bad);
        Assert.False(options.TryGetInt32(key: "empty", out _));
        Assert.False(options.TryGetInt32(key: "missing", out _));
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace JvmBridge.Agents;

/// <summary>
/// The text after <c>=</c> in <c>-agentpath:library=text</c>, parsed as comma-separated <c>key=value</c> entries and bare flags: <c>log=debug,trace-classes,limit=5</c>.
/// Keys compare ignoring case, the first <c>=</c> splits an entry so values may contain more, whitespace around keys and values is trimmed, empty entries are skipped and the last duplicate wins.
/// Parsing never throws and needs no globalization data.
/// </summary>
public sealed class AgentOptions
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    private AgentOptions(string text)
    {
        Text = text;
    }

    /// <summary>Gets the number of entries.</summary>
    public int Count => _values.Count;

    /// <summary>Gets the keys, in no particular order.</summary>
    public IEnumerable<string> Keys => _values.Keys;

    /// <summary>Gets the unparsed text, as <see cref="AgentContext.Options"/>.</summary>
    public string Text { get; }

    /// <summary>Parses option text.</summary>
    /// <param name="text">The text after <c>=</c>, or <see langword="null"/> for none.</param>
    /// <returns>The parsed options.</returns>
    public static AgentOptions Parse(string? text)
    {
        AgentOptions options = new(text ?? string.Empty);

        foreach (string entry in options.Text.Split(','))
        {
            string trimmed = entry.Trim();

            if (trimmed.Length == 0)
                continue;

            int separator = trimmed.IndexOf('=', StringComparison.Ordinal);

            if (separator < 0)
                options._values[trimmed] = string.Empty;
            else if (separator > 0)
                options._values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }

        return options;
    }

    /// <summary>Gets whether an entry exists, with or without a value.</summary>
    /// <param name="key">The entry's key.</param>
    /// <returns><see langword="true"/> when the key is present.</returns>
    public bool Contains(string key)
    {
        return _values.ContainsKey(key);
    }

    /// <summary>Gets whether a flag is on: present as a bare flag or with the value <c>true</c>, <c>on</c>, <c>yes</c> or <c>1</c>.</summary>
    /// <param name="key">The flag's key.</param>
    /// <returns><see langword="true"/> when the flag is on; an absent flag, <c>false</c>, <c>off</c>, <c>no</c> and <c>0</c> are off.</returns>
    public bool IsEnabled(string key)
    {
        if (!_values.TryGetValue(key, out string? value))
            return false;

        return value.Length == 0
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || value == "1";
    }

    /// <summary>Reads an integer value.</summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="value">The value when the entry exists and is an integer.</param>
    /// <returns><see langword="true"/> when the entry exists and parses as an invariant-culture integer.</returns>
    public bool TryGetInt32(string key, out int value)
    {
        value = 0;

        return _values.TryGetValue(key, out string? text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads a value.</summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="value">The value, which is empty for a bare flag.</param>
    /// <returns><see langword="true"/> when the key is present.</returns>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value)
    {
        return _values.TryGetValue(key, out value);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Text;
    }
}

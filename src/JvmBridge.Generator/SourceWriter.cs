using System.Collections.Generic;

namespace JvmBridge.Generator;

/// <summary>Accumulates generated source. Its members return nothing, so call sites never have to discard a builder.</summary>
internal sealed class SourceWriter
{
    private readonly List<string> _lines = [];

    public override string ToString()
    {
        return string.Join(separator: "\n", _lines) + "\n";
    }

    internal void Line(string text = "")
    {
        _lines.Add(text);
    }
}

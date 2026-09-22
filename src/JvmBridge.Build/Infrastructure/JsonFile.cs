using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace JvmBridge.Build.Infrastructure;

internal static class JsonFile
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    internal static JsonSerializerOptions Options { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static TValue Read<TValue>(string path)
    {
        TValue? value = JsonSerializer.Deserialize<TValue>(File.ReadAllText(path), Options) ?? throw new InvalidOperationException($"Could not deserialize {path}.");

        return value;
    }

    public static JsonObject ToObject<TValue>(TValue value)
    {
        JsonNode? node = JsonSerializer.SerializeToNode(value, Options);

        return node is not JsonObject result
            ? throw new InvalidOperationException($"Could not serialize {typeof(TValue).Name} as a JSON object.")
            : result;
    }

    public static void Write<TValue>(string path, TValue value)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            FileSystem.EnsureDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(value, Options) + "\n", Utf8);
    }

    public static void WriteNode(string path, JsonNode value)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            FileSystem.EnsureDirectory(directory);

        File.WriteAllText(path, value.ToJsonString(Options) + "\n", Utf8);
    }
}

using System.Text.Json.Nodes;

namespace JvmBridge.Build.Infrastructure;

internal static class JsonNodeExtensions
{
    public static JsonNode? Property(this JsonNode? node, string propertyName)
    {
        return node is JsonObject value ? value[propertyName] : null;
    }

    public static JsonArray RequireArray(this JsonNode? node, string description)
    {
        return node as JsonArray ?? throw new InvalidOperationException($"Expected JSON array for {description}.");
    }

    public static int RequireInt32(this JsonNode? node, string description)
    {
        return node?.GetValue<int>() ?? throw new InvalidOperationException($"Expected JSON integer for {description}.");
    }

    public static JsonObject RequireObject(this JsonNode? node, string description)
    {
        return node as JsonObject ?? throw new InvalidOperationException($"Expected JSON object for {description}.");
    }

    public static string RequireString(this JsonNode? node, string description)
    {
        return node?.GetValue<string>() ?? throw new InvalidOperationException($"Expected JSON string for {description}.");
    }

    public static void SetProperty(this JsonObject node, string propertyName, JsonNode? value)
    {
        node[propertyName] = value;
    }
}

using System.Text.Json.Nodes;

using JvmBridge.Build.Models;

namespace JvmBridge.Build;

internal static class CoverageValidator
{
    internal static void Validate(IReadOnlyCollection<JdkEntry> inventory, IReadOnlyCollection<JsonObject> results)
    {
        foreach (JsonObject result in results)
        {
            if (Value(result, name: "status") == "failed")
                throw new InvalidOperationException("Failed JVM cell: " + result.ToJsonString());
        }

        foreach (JdkEntry entry in inventory.Where(entry => entry.Status == "available"))
        {
            bool passed = results.Any(result => Matches(entry, result) && Value(result, name: "status") == "passed");

            if (!passed)
                throw new InvalidOperationException($"Unverified pinned JVM cell: {entry.Rid}, Java {entry.Java}, {entry.Implementation}, {entry.Version}.");
        }
    }

    private static bool Matches(JdkEntry entry, JsonObject result)
    {
        bool sameCell = Value(result, name: "rid") == entry.Rid && result[propertyName: "java"]?.GetValue<int>() == entry.Java && Value(result, name: "implementation") == entry.Implementation;
        bool sameArchive = Value(result, name: "sha256") == entry.Sha256 && Value(result, name: "distribution") == entry.Distribution && Value(result, name: "version") == entry.Version;

        return sameCell && sameArchive;
    }

    private static string? Value(JsonObject result, string name)
    {
        return result[name]?.GetValue<string>();
    }
}

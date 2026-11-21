using System.Text.Json.Nodes;

using JvmBridge.Build.Models;

namespace JvmBridge.Build;

internal static class CoverageValidator
{
    internal static void Validate(IReadOnlyCollection<JdkEntry> inventory, IReadOnlyCollection<JsonObject> results)
    {
        foreach (JsonObject cell in results)
        {
            if (Value(cell, name: "status") == "failed")
                throw new InvalidOperationException("Failed JVM cell: " + cell.ToJsonString());
        }

        foreach (JdkEntry entry in inventory.Where(entry => entry.Status == "available"))
        {
            bool passed = results.Any(cell => Matches(entry, cell) && Value(cell, name: "status") == "passed");

            if (!passed)
                throw new InvalidOperationException(message: $"Unverified pinned JVM cell: {entry.Rid}, Java {entry.Java}, {entry.Implementation}, {entry.Version}.");
        }
    }

    private static bool Matches(JdkEntry entry, JsonObject cell)
    {
        bool sameCell = Value(cell, name: "rid") == entry.Rid
            && cell[propertyName: "java"]?.GetValue<int>() == entry.Java
            && Value(cell, name: "implementation") == entry.Implementation;

        bool sameArchive = Value(cell, name: "sha256") == entry.Sha256
            && Value(cell, name: "distribution") == entry.Distribution
            && Value(cell, name: "version") == entry.Version;

        return sameCell && sameArchive;
    }

    private static string? Value(JsonObject cell, string name)
    {
        return cell[name]?.GetValue<string>();
    }
}

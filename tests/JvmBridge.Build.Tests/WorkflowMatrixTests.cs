using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Build.Tests;

/// <summary>Holds the matrices of the test and footprint workflows to the targets of <see cref="Compatibility"/>, which the compatibility page and the coverage check read.</summary>
public sealed class WorkflowMatrixTests
{
    private const string ItemStart = "- rid: ";

    /// <summary>The footprint workflow measures every agent target that runs on the runner itself, not in a container and not as x86.</summary>
    [Fact]
    public void TheFootprintMatrixListsEveryNativeAgentTarget()
    {
        string[] expected = [.. Compatibility.Configuration.Targets
            .Where(static target => target.Agent && target.Container is null && target.CrossContainer is null && target.Rid != "win-x86")
            .Select(Describe)];

        Assert.Equal(expected, ReadMatrix(workflow: "reusable-footprint.yml"));
    }

    /// <summary>The test workflow runs every target that builds or runs an agent, on the runner and in the images that the target names.</summary>
    [Fact]
    public void TheTestMatrixListsEveryAgentTarget()
    {
        string[] expected = [.. Compatibility.Configuration.Targets.Where(static target => target.Agent || target.Build).Select(Describe)];

        Assert.Equal(expected, ReadMatrix(workflow: "reusable-test.yml"));
    }

    private static string Describe(TargetConfiguration target)
    {
        return string.Join(separator: ' ', target.Rid, target.Runner ?? "-", target.Container ?? "-", target.CrossContainer ?? "-");
    }

    private static string Describe(Dictionary<string, string> item)
    {
        return string.Join(
            separator: ' ',
            item[key: "rid"],
            item.GetValueOrDefault(key: "runner", defaultValue: "-"),
            item.GetValueOrDefault(key: "container", defaultValue: "-"),
            item.GetValueOrDefault(key: "crossContainer", defaultValue: "-")
        );
    }

    // Reads the items of the workflow's matrix include list, which start with "- rid:" and continue with "key: value" lines indented below the dash.
    private static List<string> ReadMatrix(string workflow)
    {
        List<Dictionary<string, string>> items = [];
        int? itemIndent = null;

        foreach (string line in File.ReadAllLines(RepositoryContext.Discover().PathFromRoot(parts: [".github", "workflows", workflow])))
        {
            string trimmed = line.TrimStart();
            int indent = line.Length - trimmed.Length;

            if (trimmed.StartsWith(ItemStart, StringComparison.Ordinal))
            {
                items.Add(new Dictionary<string, string> { [key: "rid"] = trimmed[ItemStart.Length..].Trim() });
                itemIndent = indent;
            }
            else if (itemIndent is int start && indent > start && trimmed.IndexOf(value: ": ", StringComparison.Ordinal) is int colon and > 0)
            {
                items[^1][trimmed[..colon]] = trimmed[(colon + 2)..].Trim();
            }
            else if (trimmed.Length > 0)
            {
                itemIndent = null;
            }
        }

        return [.. items.Select(Describe)];
    }
}

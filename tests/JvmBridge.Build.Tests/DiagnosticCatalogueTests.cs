using System.Text.RegularExpressions;

using Xunit;

using JvmBridge.Build.Infrastructure;

namespace JvmBridge.Build.Tests;

/// <summary>Keeps docs/diagnostics.md in step with the diagnostic codes the sources report.</summary>
public sealed partial class DiagnosticCatalogueTests
{
    /// <summary>Every code that a source file mentions has an entry in the catalogue, and every entry is a code that the sources still report.</summary>
    [Fact]
    public void EveryReportedCodeIsDocumentedAndEveryDocumentedCodeExists()
    {
        RepositoryContext repository = RepositoryContext.Discover();
        string source = repository.PathFromRoot(parts: ["src"]);
        List<string> reported = [];

        foreach (string file in Directory.EnumerateFiles(source, searchPattern: "*", SearchOption.AllDirectories))
        {
            if (!IsAuthored(file))
                continue;

            foreach (Match match in Code().Matches(File.ReadAllText(file)))
                reported.Add(match.Value);
        }

        List<string> documented = [];

        foreach (Match match in Heading().Matches(File.ReadAllText(repository.PathFromRoot(parts: ["docs", "diagnostics.md"]))))
            documented.Add(match.Groups[groupnum: 1].Value);

        Assert.Equal(documented.Distinct().Order(), reported.Distinct().Order());
        Assert.NotEmpty(reported);
    }

    [GeneratedRegex(@"JVMB\d{3}")]
    private static partial Regex Code();

    [GeneratedRegex(@"^### (JVMB\d{3}):", RegexOptions.Multiline)]
    private static partial Regex Heading();

    private static bool IsAuthored(string file)
    {
        string normalized = file.Replace(oldChar: '\\', newChar: '/');
        bool generatedFolder = normalized.Contains(value: "/obj/", StringComparison.Ordinal) || normalized.Contains(value: "/bin/", StringComparison.Ordinal);

        return !generatedFolder && Path.GetExtension(file) is ".cs" or ".targets" or ".props" or ".md";
    }
}

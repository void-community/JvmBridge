using System.IO.Compression;
using System.Reflection;

using JvmBridge.Build.Infrastructure;

using Microsoft.Build.Framework;

using BuildTask = Microsoft.Build.Utilities.Task;

namespace JvmBridge.Tasks;

/// <summary>
/// Reads the jars named by <c>JavaReference</c> items and writes typed C# proxies for the classes their <c>Types</c> metadata selects. An entry of <c>Types</c> is an internal class name (<c>com/acme/Service</c>), a package
/// (<c>com/acme/*</c>) or a package and its subpackages (<c>com/acme/**</c>), separated by semicolons. Nested classes are not wrapped. The proxies are written by the source writer of the generator assembly, which the task loads from
/// <see cref="GeneratorAssembly"/>, so the writer is tested next to the other generators.
/// </summary>
public sealed class GenerateJavaProxies : BuildTask
{
    private const int BridgeFlag = 0x0040;
    private const int PublicFlag = 0x0001;
    private const int StaticFlag = 0x0008;
    private const int SyntheticFlag = 0x1000;

    /// <summary>Gets or sets the path of <c>JvmBridge.Generator.dll</c>, which holds the proxy source writer.</summary>
    /// <value>The full path of the assembly, which the package's targets take from its analyzers directory; empty until MSBuild sets it, and required.</value>
    [Required]
    public string GeneratorAssembly { get; set; } = string.Empty;

    /// <summary>Gets or sets the file to write.</summary>
    /// <value>The path of the C# file that receives the proxies, which is rewritten only when its content changes; empty until MSBuild sets it, and required.</value>
    [Required]
    public string OutputFile { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>JavaReference</c> items: the jar and its <c>Types</c> metadata.</summary>
    /// <value>
    /// One line for each item: the full path of the jar, a tab, the item as the project names it, a tab, and its <c>Types</c> metadata. It is text because MSBuild hands an item list only to an array; empty until MSBuild sets it,
    /// and required.
    /// </value>
    [Required]
    public string References { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override bool Execute()
    {
        List<string> description = [];
        bool succeeded = true;

        foreach (string reference in References.Split(separator: '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            succeeded &= Read(reference, description);

        if (!succeeded)
            return false;

        string source = WriteSource(string.Join(separator: "\n", description) + "\n");

        FileSystem.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(OutputFile)) ?? ".");

        if (!File.Exists(OutputFile) || File.ReadAllText(OutputFile) != source)
            File.WriteAllText(OutputFile, source);

        return true;
    }

    private static void Describe(List<string> description, string className, ClassSummary file)
    {
        if ((file.AccessFlags & PublicFlag) == 0)
            return;

        description.Add("class\t" + className);

        foreach (ClassMethodSummary method in file.Methods)
        {
            bool exposed = (method.AccessFlags & PublicFlag) != 0
                && (method.AccessFlags & (SyntheticFlag | BridgeFlag)) == 0
                && method.Name != "<clinit>";

            if (!exposed)
                continue;

            string kind = (method.AccessFlags & StaticFlag) != 0 ? "static" : "instance";

            description.Add("member\t" + method.Name + "\t" + method.Descriptor + "\t" + kind);
        }
    }

    private static bool IsWrappable(string className)
    {
        bool nested = className.Contains(value: '$', StringComparison.Ordinal);

        return !nested
            && !className.EndsWith(value: "module-info", StringComparison.Ordinal)
            && !className.EndsWith(value: "package-info", StringComparison.Ordinal);
    }

    private static bool Matches(string className, string[] patterns, bool[] used)
    {
        bool matched = false;

        for (int index = 0; index < patterns.Length; index++)
        {
            string pattern = patterns[index];
            bool recursive = pattern.EndsWith(value: "/**", StringComparison.Ordinal);
            bool flat = pattern.EndsWith(value: "/*", StringComparison.Ordinal) && !recursive;
            bool match = className == pattern;

            if (recursive)
            {
                match = className.StartsWith(pattern[..^2], StringComparison.Ordinal);
            }
            else if (flat)
            {
                match = className.StartsWith(pattern[..^1], StringComparison.Ordinal)
                    && !className.AsSpan(pattern.Length - 1).Contains(value: '/');
            }

            if (match)
            {
                used[index] = true;
                matched = true;
            }
        }

        return matched;
    }

    private bool Fail(string code, string message)
    {
        Log.LogError(subcategory: null,
            code,
            helpKeyword: null,
            file: null,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message);

        return false;
    }

    private bool Read(string reference, List<string> description)
    {
        // A line of References starts with the jar and the item's own spelling, which the messages name it by.
        string[] fields = reference.Split(separator: '\t');
        string path = fields[0];
        string item = fields.Length > 1 ? fields[1] : path;

        // The classes to wrap, which an item names in its Types metadata or not at all.
        string types = fields.Length > 2 ? fields[2] : string.Empty;
        string[] patterns = types.Split(separator: ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (patterns.Length == 0)
        {
            return Fail(code: "JVMB011",
                $"The JavaReference {item} needs Types metadata that lists the classes to wrap, for example Types=\"com/acme/Service;com/acme/model/*\".");
        }

        if (!File.Exists(path))
            return Fail(code: "JVMB013", $"The JavaReference {item} does not exist: {path}");

        bool[] used = new bool[patterns.Length];

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);

            foreach (ZipArchiveEntry entry in archive.Entries
                .Where(static candidate => candidate.FullName.EndsWith(value: ".class", StringComparison.Ordinal)))
            {
                string className = entry.FullName[..^".class".Length];

                if (!IsWrappable(className) || !Matches(className, patterns, used))
                    continue;

                using Stream stream = entry.Open();

                using MemoryStream bytes = new();

                stream.CopyTo(bytes);
                Describe(description, className, ClassSummaryReader.Read(bytes.ToArray()));
            }
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Fail(code: "JVMB013", $"Could not read the JavaReference {item}: {failure.Message}");
        }

        for (int index = 0; index < patterns.Length; index++)
        {
            if (!used[index])
            {
                return Fail(code: "JVMB012",
                    $"The Types entry '{patterns[index]}' of the JavaReference {item} matches no class in the jar.");
            }
        }

        return true;
    }

    private string WriteSource(string description)
    {
        Assembly generator = Assembly.LoadFrom(GeneratorAssembly);

        Type writer = generator.GetType(name: "JvmBridge.Generator.Proxies.JavaProxySourceWriter", throwOnError: true)
            ?? throw new InvalidOperationException(message: "The proxy source writer is missing.");

        MethodInfo method = writer.GetMethod(name: "GenerateFromText", types: [typeof(string)])
            ?? throw new InvalidOperationException(message: "The proxy source writer has no GenerateFromText method.");

        return method.CreateDelegate<Func<string, string>>()(description);
    }
}

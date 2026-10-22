namespace JvmBridge.Testing;

internal static class SourceGlob
{
    /// <summary>Finds the files a pattern such as <c>Fixtures/**/*.java</c> names: the directory before the first wildcard segment is searched, recursively when a <c>**</c> segment follows.</summary>
    internal static string[] Resolve(string pattern, IEnumerable<string> roots)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        string[] segments = pattern.Replace(oldChar: '\\', newChar: '/').Split(separator: '/');
        int wildcard = Array.FindIndex(segments, static segment => segment.Contains(value: '*', StringComparison.Ordinal));

        foreach (string root in roots)
        {
            string[] files = wildcard < 0 ? ResolvePath(root, pattern) : ResolveWildcard(root, segments, wildcard);

            if (files.Length > 0)
                return files;
        }

        throw new FileNotFoundException("No Java sources match '" + pattern + "' relative to " + string.Join(separator: " or ", roots) + ".");
    }

    private static string[] ResolvePath(string root, string pattern)
    {
        string path = Path.GetFullPath(pattern, root);

        return File.Exists(path)
            ? [path]
            : Directory.Exists(path) ? [.. Directory.EnumerateFiles(path, searchPattern: "*.java", SearchOption.AllDirectories)] : [];
    }

    private static string[] ResolveWildcard(string root, string[] segments, int wildcard)
    {
        string directory = Path.GetFullPath(string.Join(separator: '/', segments.Take(wildcard)), root);

        if (!Directory.Exists(directory))
            return [];

        bool recursive = segments.Skip(wildcard).Any(static segment => segment == "**");
        SearchOption search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        return [.. Directory.EnumerateFiles(directory, segments[^1], search)];
    }
}

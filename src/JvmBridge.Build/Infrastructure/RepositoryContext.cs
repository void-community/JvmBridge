namespace JvmBridge.Build.Infrastructure;

internal sealed class RepositoryContext
{
    private RepositoryContext(string root)
    {
        Root = root;
    }

    public string Artifacts => Path.Combine(Root, path2: "artifacts");

    public string Root { get; }

    public static RepositoryContext Discover()
    {
        string? root = (FindRoot(Directory.GetCurrentDirectory()) ?? FindRoot(AppContext.BaseDirectory)) ?? throw new InvalidOperationException(message: "Could not locate the JvmBridge repository root.");

        return new RepositoryContext(root);
    }

    public string PathFromRoot(params string[] parts)
    {
        string path = Root;

        foreach (string part in parts)
            path = Path.Combine(path, part);

        return path;
    }

    private static string? FindRoot(string start)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(start));

        while (directory is not null)
        {
            bool hasSolution = File.Exists(Path.Combine(directory.FullName, path2: "JvmBridge.slnx"));
            bool hasConfiguration = File.Exists(Path.Combine(directory.FullName, path2: "src", path3: "JvmBridge.Build", path4: "compatibility.json"));

            if (hasSolution && hasConfiguration)
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}

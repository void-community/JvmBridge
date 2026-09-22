namespace JvmBridge.Build.Infrastructure;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string prefix)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString(format: "N"));
        FileSystem.EnsureDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

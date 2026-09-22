namespace JvmBridge.Build.Infrastructure;

internal static class FileSystem
{
    public static void EnsureDirectory(string path)
    {
        DirectoryInfo directory = Directory.CreateDirectory(path);

        if (!directory.Exists)
            throw new IOException("Could not create directory: " + path);
    }
}

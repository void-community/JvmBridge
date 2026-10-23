using System.IO.Compression;

namespace JvmBridge.Tool.Commands;

/// <summary>Reads the class files of the paths a command is given.</summary>
internal static class ClassInputs
{
    private const string ClassExtension = ".class";

    /// <summary>Gets whether a path names a jar or zip archive by its extension.</summary>
    /// <param name="path">A file name, absolute or relative; only its extension is read.</param>
    /// <returns><see langword="true"/> for a .jar or .zip file name.</returns>
    internal static bool IsArchive(string path)
    {
        bool jar = path.EndsWith(value: ".jar", StringComparison.OrdinalIgnoreCase);
        bool zip = path.EndsWith(value: ".zip", StringComparison.OrdinalIgnoreCase);

        return jar || zip;
    }

    /// <summary>Adds the classes of a path: a class file, a directory searched recursively, or a jar.</summary>
    /// <param name="path">A class file, a directory or a jar on disk, as the command line named it.</param>
    /// <param name="classes">The list to add to.</param>
    /// <param name="error">Where problems are written.</param>
    /// <returns><see langword="false"/> when the path does not exist or cannot be read.</returns>
    internal static bool TryRead(string path, List<ClassInput> classes, TextWriter error)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (string file in Directory.EnumerateFiles(path, "*" + ClassExtension, SearchOption.AllDirectories))
                    classes.Add(new ClassInput(Path.GetRelativePath(path, file), File.ReadAllBytes(file)));

                return true;
            }

            if (!File.Exists(path))
            {
                error.WriteLine($"jvmbridge: {path} does not exist.");

                return false;
            }

            if (!IsArchive(path))
            {
                classes.Add(new ClassInput(Path.GetFileName(path), File.ReadAllBytes(path)));

                return true;
            }

            using ZipArchive archive = ZipFile.OpenRead(path);

            foreach (ZipArchiveEntry entry in archive.Entries.Where(static candidate => candidate.FullName.EndsWith(ClassExtension, StringComparison.Ordinal)))
            {
                using MemoryStream bytes = new();

                using (Stream stream = entry.Open())
                    stream.CopyTo(bytes);

                classes.Add(new ClassInput(entry.FullName, bytes.ToArray()));
            }

            return true;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            error.WriteLine($"jvmbridge: could not read {path}: {failure.Message}");

            return false;
        }
    }
}

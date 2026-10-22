using System.IO.Compression;

namespace JvmBridge.Agents;

/// <summary>Writes helper classes to a temporary jar that can be appended to the JVM's bootstrap search path, which works on every JVM unlike defining a class with a null loader.</summary>
internal static class BootstrapJar
{
    internal static string Create(IReadOnlyList<JavaClassDefinition> classes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"jvmbridge-{Environment.ProcessId}-{Guid.NewGuid():N}.jar");

        try
        {
            using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using ZipArchive archive = new(file, ZipArchiveMode.Create);

            foreach (JavaClassDefinition definition in classes)
            {
                ZipArchiveEntry entry = archive.CreateEntry($"{definition.Name}.class", CompressionLevel.NoCompression);
                using Stream stream = entry.Open();
                stream.Write(definition.Bytes.Span);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Delete(path);

            throw;
        }

        return path;
    }

    internal static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The JVM may still hold the jar open, as on Windows; a temporary file is not worth failing shutdown.
        }
    }
}

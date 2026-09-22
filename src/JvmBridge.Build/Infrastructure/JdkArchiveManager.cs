using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

using JvmBridge.Build.Models;

namespace JvmBridge.Build.Infrastructure;

internal sealed class JdkArchiveManager(HttpService http)
{
    private readonly HttpService _http = http;

    public async Task<string> UnpackAsync(JdkArchive archive, string directory, CancellationToken cancellationToken)
    {
        FileSystem.EnsureDirectory(directory);
        bool zipped = archive.Url.EndsWith(value: ".zip", StringComparison.OrdinalIgnoreCase);
        string archivePath = Path.Combine(directory, zipped ? "jdk.zip" : "jdk.tar.gz");
        string downloadLog = Path.Combine(directory, path2: "download.log");

        try
        {
            await _http.DownloadFileAsync(archive.Url, archivePath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            TextFile.Write(downloadLog, $"Downloaded {archive.Url}\n");
        }
        catch (Exception failure)
        {
            TextFile.Write(downloadLog, failure + "\n");

            throw;
        }

        using (FileStream stream = File.OpenRead(archivePath))
        {
            string actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));

            if (!string.Equals(actual, archive.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("JDK SHA256 mismatch: " + archive.Url);
        }

        string target = Path.Combine(directory, path2: "jdk");
        FileSystem.EnsureDirectory(target);

        if (zipped)
            ExtractZip(archivePath, target);
        else
            ExtractTarGzip(archivePath, target);

        File.Delete(archivePath);

        string executable = OperatingSystem.IsWindows() ? "java.exe" : "java";

        string? home = Directory.EnumerateFiles(target, executable, SearchOption.AllDirectories)
            .Where(
                static path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), b: "bin", StringComparison.Ordinal) && File.Exists(Path.Combine(Directory.GetParent(path)?.Parent?.FullName ?? string.Empty, path2: "include", path3: "jni.h"))
            )
            .Select(static path => Directory.GetParent(path)?.Parent?.FullName)
            .Where(static path => path is not null)
            .Cast<string>()
            .OrderBy(static path => path.Length)
            .FirstOrDefault();

        return home ?? throw new InvalidOperationException(message: "No JDK home in archive");
    }

    private static void ExtractTarGzip(string archivePath, string target)
    {
        using FileStream archive = File.OpenRead(archivePath);

        using GZipStream gzip = new(archive, CompressionMode.Decompress);

        // The framework extractor validates resolved link targets and preserves modes and hard links.
        TarFile.ExtractToDirectory(gzip, target, overwriteFiles: false);
    }

    private static void ExtractZip(string archivePath, string target)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destination = SafeDestination(target, entry.FullName);

            if (entry.FullName.EndsWith(value: '/') || string.IsNullOrEmpty(entry.Name))
            {
                FileSystem.EnsureDirectory(destination);

                continue;
            }

            string? parent = Path.GetDirectoryName(destination);

            if (!string.IsNullOrEmpty(parent))
                FileSystem.EnsureDirectory(parent);

            using Stream input = entry.Open();

            using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            input.CopyTo(output);
        }
    }

    private static string SafeDestination(string root, string name)
    {
        if (Path.IsPathRooted(name))
            throw new InvalidOperationException("Unsafe archive path: " + name);

        string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar.ToString();
        string destination = Path.GetFullPath(Path.Combine(root, name));

        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool outsideRoot = !destination.StartsWith(fullRoot, comparison) && !string.Equals(destination, fullRoot[..^1], comparison);

        return outsideRoot ? throw new InvalidOperationException("Unsafe archive path: " + name) : destination;
    }
}

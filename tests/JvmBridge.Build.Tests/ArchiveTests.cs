using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Build.Tests;

/// <summary>
/// Verifies archive integrity and extraction boundaries for downloaded JDKs.
/// </summary>
public sealed class ArchiveTests
{
    /// <summary>
    /// Verifies that a downloaded archive must match its pinned digest before extraction.
    /// </summary>
    [Fact]
    public async Task ChecksumMismatchIsRejectedAsync()
    {
        byte[] payload = CreateZip(entryName: "jdk/bin/java", contents: "fixture");

        using PayloadHandler handler = new(payload);

        using HttpClient client = new(handler);

        using HttpService httpService = new(client);

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-checksum-test-");

        JdkArchive archive = Archive(new string(c: '0', count: 64));
        JdkArchiveManager manager = new(httpService);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UnpackAsync(archive, temporary.Path, CancellationToken.None));

        Assert.Contains(expectedSubstring: "SHA256 mismatch", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A TAR link cannot redirect extraction outside the destination.</summary>
    [Fact]
    public async Task EscapingTarLinkIsRejectedAsync()
    {
        using MemoryStream buffer = new();

        using (GZipStream compression = new(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            using (TarWriter writer = new(compression, leaveOpen: true))
            {
                await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, entryName: "escape") { LinkName = ".." }, CancellationToken.None);
                await writer.WriteEntryAsync(FileEntry(name: "escape/escaped.txt", contents: "unsafe"), CancellationToken.None);
            }
        }

        byte[] payload = buffer.ToArray();

        using PayloadHandler handler = new(payload);

        using HttpClient client = new(handler);

        using HttpService service = new(client);

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-tar-link-test-");

        JdkArchiveManager manager = new(service);

        IOException failure = await Assert.ThrowsAnyAsync<IOException>(
            () => manager.UnpackAsync(Archive(Convert.ToHexStringLower(SHA256.HashData(payload)), extension: ".tar.gz"), temporary.Path, CancellationToken.None)
        );

        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
        Assert.False(File.Exists(Path.Combine(temporary.Path, path2: "escaped.txt")));
    }

    /// <summary>
    /// Verifies that TAR directories, links, and executable modes survive managed extraction.
    /// </summary>
    [Fact]
    public async Task TarMetadataIsPreservedAsync()
    {
        string executableName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        byte[] payload = CreateTarGzip(executableName);

        using PayloadHandler handler = new(payload);

        using HttpClient client = new(handler);

        using HttpService httpService = new(client);

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-tar-test-");

        JdkArchive archive = Archive(Convert.ToHexStringLower(SHA256.HashData(payload)), extension: ".tar.gz");
        JdkArchiveManager manager = new(httpService);
        string home = await manager.UnpackAsync(archive, temporary.Path, CancellationToken.None);
        string java = Path.Combine(home, path2: "bin", executableName);

        Assert.Equal(expected: "java", await File.ReadAllTextAsync(java, CancellationToken.None));
        Assert.Equal(expected: "java", await File.ReadAllTextAsync(Path.Combine(home, path2: "bin", path3: "java-copy"), CancellationToken.None));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserExecute, File.GetUnixFileMode(java) & UnixFileMode.UserExecute);
            Assert.Equal(executableName, new FileInfo(Path.Combine(home, path2: "bin", path3: "javac")).LinkTarget);
        }
    }

    /// <summary>
    /// Verifies that a ZIP entry cannot escape the selected extraction directory.
    /// </summary>
    [Fact]
    public async Task TraversalEntryIsRejectedAsync()
    {
        byte[] payload = CreateZip(entryName: "../escaped.txt", contents: "unsafe");

        using PayloadHandler handler = new(payload);

        using HttpClient client = new(handler);

        using HttpService httpService = new(client);

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-traversal-test-");

        JdkArchive archive = Archive(Convert.ToHexStringLower(SHA256.HashData(payload)));
        JdkArchiveManager manager = new(httpService);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UnpackAsync(archive, temporary.Path, CancellationToken.None));

        Assert.Contains(expectedSubstring: "Unsafe archive path", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(temporary.Path, path2: "escaped.txt")));
    }

    private static JdkArchive Archive(string checksum, string extension = ".zip")
    {
        return new JdkArchive
        {
            Distribution = "fixture",
            Version = "1",
            Url = "https://example.invalid/jdk" + extension,
            Sha256 = checksum
        };
    }

    private static byte[] CreateTarGzip(string executableName)
    {
        using MemoryStream buffer = new();

        using (GZipStream compression = new(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            using (TarWriter writer = new(compression, leaveOpen: true))
            {
                foreach (string directory in new[] { "jdk/", "jdk/bin/", "jdk/include/" })
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, directory));

                PaxTarEntry java = FileEntry("jdk/bin/" + executableName, contents: "java");
                java.Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                writer.WriteEntry(java);
                writer.WriteEntry(FileEntry(name: "jdk/include/jni.h", contents: "header"));
                writer.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, entryName: "jdk/bin/java-copy") { LinkName = "jdk/bin/" + executableName });

                if (!OperatingSystem.IsWindows())
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, entryName: "jdk/bin/javac") { LinkName = executableName });
            }
        }

        return buffer.ToArray();
    }

    private static byte[] CreateZip(string entryName, string contents)
    {
        using MemoryStream buffer = new();

        using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName);

            using StreamWriter writer = new(entry.Open());

            writer.Write(contents);
        }

        return buffer.ToArray();
    }

    private static PaxTarEntry FileEntry(string name, string contents)
    {
        return new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(contents)) };
    }

    private sealed class PayloadHandler(byte[] payload) : HttpMessageHandler
    {
        private readonly byte[] _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(_payload), RequestMessage = request };

            return Task.FromResult(response);
        }
    }
}

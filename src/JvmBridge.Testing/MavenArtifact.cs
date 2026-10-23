using System.Collections.Concurrent;
using System.Security.Cryptography;

using JvmBridge.Build.Infrastructure;

namespace JvmBridge.Testing;

/// <summary>
/// A jar or other file from a Maven repository, pinned by checksum, for tests that run patches and link checks against the real classes of an application: <c>new MavenArtifact("https://maven.minecraftforge.net", "net.minecraftforge:eventbus:0.10.3",
/// sha256)</c>. It is downloaded once per machine and cached under the temporary directory, like the pinned JDKs; a file whose checksum differs fails the test instead of being used.
/// </summary>
public sealed class MavenArtifact
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Paths = new(StringComparer.Ordinal);

    /// <summary>Pins an artifact.</summary>
    /// <param name="repository">The repository's base address, for example <c>https://repo1.maven.org/maven2</c>.</param>
    /// <param name="coordinates"><c>group:artifact:version</c>, with an optional <c>:classifier</c> and <c>@extension</c> (<c>jar</c> by default), for example <c>net.minecraftforge:forge:1.12.2-14.23.5.2860:universal</c>.</param>
    /// <param name="sha256">The SHA-256 of the file, in hexadecimal.</param>
    public MavenArtifact(string repository, string coordinates, string sha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(repository);
        ArgumentException.ThrowIfNullOrEmpty(coordinates);
        ArgumentException.ThrowIfNullOrEmpty(sha256);

        string extension = "jar";
        string location = coordinates;
        int at = location.IndexOf(value: '@', StringComparison.Ordinal);

        if (at >= 0)
        {
            extension = location[(at + 1)..];
            location = location[..at];
        }

        string[] parts = location.Split(separator: ':');

        if (parts.Length is < 3 or > 4 || parts.Any(static part => part.Length == 0) || extension.Length == 0)
            throw new ArgumentException($"'{coordinates}' is not group:artifact:version[:classifier][@extension].", nameof(coordinates));

        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException(message: "The checksum must be 64 hexadecimal digits of SHA-256.", nameof(sha256));

        Coordinates = coordinates;
        Sha256 = sha256.ToUpperInvariant();
        FileName = parts[1] + "-" + parts[2] + (parts.Length == 4 ? "-" + parts[3] : string.Empty) + "." + extension;
        Url = new Uri(
            repository.TrimEnd(trimChar: '/') + "/" + parts[0].Replace(oldChar: '.', newChar: '/') + "/" + parts[1] + "/" + parts[2] + "/" + FileName
        );
    }

    /// <summary>Gets the coordinates as they were given.</summary>
    public string Coordinates { get; }

    /// <summary>Gets the name of the file, for example <c>eventbus-0.10.3.jar</c>.</summary>
    public string FileName { get; }

    /// <summary>Gets the pinned checksum, in uppercase hexadecimal.</summary>
    public string Sha256 { get; }

    /// <summary>Gets the address the file is downloaded from.</summary>
    public Uri Url { get; }

    /// <summary>Gets the path of the cached file, downloading and checking it the first time.</summary>
    /// <param name="cancellationToken">Cancels waiting for the file.</param>
    /// <returns>The path of the file, which tests must not change.</returns>
    /// <exception cref="InvalidOperationException">The downloaded file has another checksum than the pinned one.</exception>
    public Task<string> GetPathAsync(CancellationToken cancellationToken = default)
    {
        Lazy<Task<string>> pending = Paths.GetOrAdd(Sha256 + FileName, static (key, artifact) => new Lazy<Task<string>>(artifact.DownloadAsync), this);

        return pending.Value.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Coordinates;
    }

    private static async Task<string> HashAsync(string path)
    {
        using FileStream stream = File.OpenRead(path);

        byte[] hash = await SHA256.HashDataAsync(stream).ConfigureAwait(continueOnCapturedContext: false);

        return Convert.ToHexString(hash);
    }

    private async Task<string> DownloadAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), path2: "jvmbridge-testing", path3: "maven", Sha256[..16]);
        string path = Path.Combine(directory, FileName);

        if (File.Exists(path) && await HashAsync(path).ConfigureAwait(continueOnCapturedContext: false) == Sha256)
            return path;

        FileSystem.EnsureDirectory(directory);

        string staging = path + ".download-" + Guid.NewGuid().ToString(format: "N");

        using (HttpService http = new())
            await http.DownloadFileAsync(Url.AbsoluteUri, staging, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

        string actual = await HashAsync(staging).ConfigureAwait(continueOnCapturedContext: false);

        if (actual != Sha256)
        {
            File.Delete(staging);

            throw new InvalidOperationException($"{Url} has the SHA-256 {actual}, not the pinned {Sha256}.");
        }

        File.Move(staging, path, overwrite: true);

        return path;
    }
}

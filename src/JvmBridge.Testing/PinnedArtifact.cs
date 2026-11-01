using System.Collections.Concurrent;
using System.Security.Cryptography;

using JvmBridge.Build.Infrastructure;

namespace JvmBridge.Testing;

/// <summary>
/// A file at an HTTP or HTTPS address, pinned by its SHA-256, for tests that run patches and link checks against the real jars of an application that are served outside any Maven repository: a release asset, a vendor download, an object addressed by its
/// hash such as <c>new PinnedArtifact(new Uri("https://launcher.mojang.com/v1/objects/&lt;sha1&gt;/server.jar"), sha256, fileName: "server-1.12.2.jar")</c>. It is downloaded once per machine and cached under the temporary directory, in a directory named
/// after its checksum, so files that share a name never collide; a file whose checksum differs fails the test instead of being used. <see cref="MavenArtifact"/> is the case whose address comes from Maven coordinates.
/// </summary>
public class PinnedArtifact
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Paths = new(StringComparer.Ordinal);

    /// <summary>Pins a file.</summary>
    /// <param name="url">The absolute HTTP or HTTPS address of the file.</param>
    /// <param name="sha256">The SHA-256 of the file, in hexadecimal.</param>
    /// <param name="fileName">The name of the cached file, or <see langword="null"/> for the last segment of the address; give one when the address ends in a name many files share, so that paths in test output tell them apart.</param>
    public PinnedArtifact(Uri url, string sha256, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrEmpty(sha256);

        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException($"'{url}' is not an absolute HTTP or HTTPS address.", nameof(url));

        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException(message: "The checksum must be 64 hexadecimal digits of SHA-256.", nameof(sha256));

        string name = fileName ?? Path.GetFileName(Uri.UnescapeDataString(url.AbsolutePath));

        if (name.Length == 0)
            name = "artifact";

        if (name is "." or ".." || name.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\']) >= 0)
            throw new ArgumentException($"'{name}' is not a file name.", nameof(fileName));

        Url = url;
        Sha256 = sha256.ToUpperInvariant();
        FileName = name;
    }

    /// <summary>Gets the name of the cached file, for example <c>eventbus-0.10.3.jar</c>.</summary>
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
        Lazy<Task<string>> pending = Paths.GetOrAdd(Sha256 + "/" + FileName, static (key, artifact) => new Lazy<Task<string>>(artifact.DownloadAsync), this);

        return pending.Value.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Url.AbsoluteUri;
    }

    private static async Task<string> HashAsync(string path)
    {
        using FileStream stream = File.OpenRead(path);

        byte[] hash = await SHA256.HashDataAsync(stream).ConfigureAwait(continueOnCapturedContext: false);

        return Convert.ToHexString(hash);
    }

    private async Task<string> DownloadAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), path2: "jvmbridge-testing", path3: "artifacts", Sha256[..16]);
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

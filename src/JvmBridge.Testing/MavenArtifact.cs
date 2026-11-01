namespace JvmBridge.Testing;

/// <summary>
/// A jar or other file from a Maven repository, pinned by checksum: the <see cref="PinnedArtifact"/> whose address comes from Maven coordinates, such as <c>new MavenArtifact("https://maven.minecraftforge.net", "net.minecraftforge:eventbus:0.10.3",
/// sha256)</c>. It is downloaded, cached and checked as any pinned artifact is.
/// </summary>
public sealed class MavenArtifact : PinnedArtifact
{
    /// <summary>Pins an artifact.</summary>
    /// <param name="repository">The repository's base address, for example <c>https://repo1.maven.org/maven2</c>.</param>
    /// <param name="coordinates"><c>group:artifact:version</c>, with an optional <c>:classifier</c> and <c>@extension</c> (<c>jar</c> by default), for example <c>net.minecraftforge:forge:1.12.2-14.23.5.2860:universal</c>.</param>
    /// <param name="sha256">The SHA-256 of the file, in hexadecimal.</param>
    public MavenArtifact(string repository, string coordinates, string sha256)
        : base(Locate(repository, coordinates, out string fileName), sha256, fileName)
    {
        Coordinates = coordinates;
    }

    /// <summary>Gets the coordinates as they were given.</summary>
    public string Coordinates { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Coordinates;
    }

    private static Uri Locate(string repository, string coordinates, out string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(repository);
        ArgumentException.ThrowIfNullOrEmpty(coordinates);

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

        fileName = parts[1] + "-" + parts[2] + (parts.Length == 4 ? "-" + parts[3] : string.Empty) + "." + extension;

        return new Uri(
            repository.TrimEnd(trimChar: '/') + "/" + parts[0].Replace(oldChar: '.', newChar: '/') + "/" + parts[1] + "/" + parts[2] + "/" + fileName
        );
    }
}

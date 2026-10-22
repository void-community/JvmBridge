using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace JvmBridge.Launcher;

/// <summary>
/// Writes the jar that <c>java -javaagent</c> starts: the launcher class, an index of the native libraries and the libraries themselves, one directory per runtime identifier. The launcher class gets a name of its own in every jar,
/// <c>jvmbridge.L</c> and seven hexadecimal digits of a hash of the libraries, because the JVM loads the class of the first <c>-javaagent</c> jar for every jar that names the same class, and with it the first jar's libraries.
/// </summary>
internal static class LauncherJar
{
    /// <summary>The runtime identifiers that <c>Launcher</c> can detect.</summary>
    internal static readonly IReadOnlyList<string> KnownRids =
    [
        "linux-arm",
        "linux-arm64",
        "linux-musl-arm64",
        "linux-musl-x64",
        "linux-x64",
        "osx-arm64",
        "osx-x64",
        "win-arm64",
        "win-x64",
        "win-x86"
    ];

    private const string LauncherName = "jvmbridge/Launcher";

    private static readonly DateTimeOffset Timestamp = new(year: 1980, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    /// <summary>Reads the compiled launcher class that the tool carries, under its own name <c>jvmbridge/Launcher</c>.</summary>
    /// <returns>The class-file bytes.</returns>
    internal static byte[] ReadLauncher()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name: "Launcher.class") ?? throw new InvalidOperationException(message: "The tool does not carry Launcher.class.");

        using MemoryStream bytes = new();

        stream.CopyTo(bytes);

        return bytes.ToArray();
    }

    /// <summary>Gives the launcher class another name of the same length, by replacing the one constant that holds it.</summary>
    /// <param name="launcher">The class file named <c>jvmbridge/Launcher</c>.</param>
    /// <param name="name">The new internal name, as long as the old one.</param>
    /// <returns>The renamed class file.</returns>
    internal static byte[] Rename(byte[] launcher, string name)
    {
        byte[] old = Encoding.ASCII.GetBytes(LauncherName);
        byte[] replacement = Encoding.ASCII.GetBytes(name);

        if (replacement.Length != old.Length)
            throw new ArgumentException($"The name must be {old.Length} characters long.", nameof(name));

        // A Utf8 constant is the tag 1, a two-byte length and the characters; this one holds the name of the class.
        byte[] constant = [1, 0, (byte)old.Length, .. old];
        int position = launcher.AsSpan().IndexOf(constant);

        if (position < 0 || launcher.AsSpan(position + 1).IndexOf(constant) >= 0)
            throw new InvalidOperationException(message: "Launcher.class must hold its name in exactly one constant.");

        byte[] renamed = [.. launcher];

        replacement.CopyTo(renamed, position + 3);

        return renamed;
    }

    /// <summary>Writes the jar, which is byte-identical for identical inputs.</summary>
    /// <param name="output">The jar to write.</param>
    /// <param name="libraries">The native libraries, at most one per runtime identifier.</param>
    /// <returns>The name of the launcher class in the jar, for example <c>jvmbridge.L1a2b3c4</c>.</returns>
    internal static string Write(string output, IReadOnlyList<AgentLibrary> libraries)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(output));

        if (directory is not null)
            new DirectoryInfo(directory).Create();

        List<AgentLibrary> ordered = [.. libraries.OrderBy(static library => library.Rid, StringComparer.Ordinal)];
        string name = LauncherName[..^"Launcher".Length] + "L" + Fingerprint(ordered);
        string resources = name + "/";

        using FileStream file = File.Create(output);

        using ZipArchive archive = new(file, ZipArchiveMode.Create);

        Add(
            archive,
            name: "META-INF/MANIFEST.MF",
            Encoding.ASCII.GetBytes(
                $"Manifest-Version: 1.0\r\nPremain-Class: {name.Replace(oldChar: '/', newChar: '.')}\r\nAgent-Class: {name.Replace(oldChar: '/', newChar: '.')}\r\n\r\n"
            )
        );
        Add(archive, name + ".class", Rename(ReadLauncher(), name));

        List<string> index = [];

        foreach (AgentLibrary library in ordered)
        {
            string fileName = Path.GetFileName(library.Path);

            index.Add($"{library.Rid}={fileName}\n");
            Add(archive, $"{resources}native/{library.Rid}/{fileName}", File.ReadAllBytes(library.Path));
        }

        Add(archive, resources + "native.properties", Encoding.UTF8.GetBytes(string.Concat(index)));

        return name.Replace(oldChar: '/', newChar: '.');
    }

    private static void Add(ZipArchive archive, string name, byte[] content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);

        entry.LastWriteTime = Timestamp;

        using Stream stream = entry.Open();

        stream.Write(content);
    }

    private static string Fingerprint(List<AgentLibrary> ordered)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (AgentLibrary library in ordered)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(library.Rid + "=" + Path.GetFileName(library.Path) + "\n"));
            hash.AppendData(File.ReadAllBytes(library.Path));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..7];
    }
}

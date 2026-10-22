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

    /// <summary>Puts libraries together for one jar: each runtime identifier once, and only the ones the launcher can detect. The same library given twice for a platform counts once, so jars that overlap can be combined.</summary>
    /// <param name="libraries">The libraries, from the command line or from other jars.</param>
    /// <returns>The libraries for the jar.</returns>
    /// <exception cref="InvalidDataException">A runtime identifier is unknown or has two different libraries, or there is no library at all.</exception>
    internal static IReadOnlyList<AgentLibrary> Combine(IEnumerable<AgentLibrary> libraries)
    {
        List<AgentLibrary> combined = [];

        foreach (AgentLibrary library in libraries)
        {
            if (!KnownRids.Contains(library.Rid))
            {
                throw new ArgumentException(
                    $"{library.Rid} is not a runtime identifier that the launcher detects. Known: {string.Join(separator: ", ", KnownRids)}.",
                    nameof(libraries)
                );
            }

            AgentLibrary? existing = combined.FirstOrDefault(candidate => candidate.Rid == library.Rid);

            if (existing is null)
            {
                combined.Add(library);

                continue;
            }

            bool same = existing.FileName == library.FileName && existing.Bytes.AsSpan().SequenceEqual(library.Bytes);

            if (!same)
                throw new InvalidDataException($"{library.Rid} is given more than once, with different libraries.");
        }

        return combined.Count == 0 ? throw new InvalidDataException(message: "There is no agent library to pack.") : combined;
    }

    /// <summary>Reads the libraries of a jar that <see cref="Write"/> wrote, to combine them with others.</summary>
    /// <param name="jar">The path of the jar.</param>
    /// <returns>The libraries in the jar.</returns>
    /// <exception cref="InvalidDataException">The file is not such a jar.</exception>
    internal static IReadOnlyList<AgentLibrary> Read(string jar)
    {
        using ZipArchive archive = ZipFile.OpenRead(jar);

        string manifest = ReadText(archive, name: "META-INF/MANIFEST.MF") ?? throw new InvalidDataException($"{jar} has no manifest, so it is not a jar from jvmbridge pack.");
        string prefix = "Premain-Class: ";
        string? launcher = manifest.Split(separator: '\n').Select(static line => line.TrimEnd(trimChar: '\r')).FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

        if (launcher is null || !launcher.StartsWith(value: "jvmbridge.L", StringComparison.Ordinal))
            throw new InvalidDataException($"{jar} has no JvmBridge launcher, so it is not a jar from jvmbridge pack.");

        string directory = launcher.Replace(oldChar: '.', newChar: '/') + "/";
        string index = ReadText(archive, directory + "native.properties") ?? throw new InvalidDataException($"{jar} has no {directory}native.properties.");
        List<AgentLibrary> libraries = [];

        foreach (string line in index.Split(separator: '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = line.IndexOf(value: '=', StringComparison.Ordinal);

            if (separator <= 0)
                throw new InvalidDataException($"{jar} has a malformed index line '{line}'.");

            string rid = line[..separator];
            string fileName = line[(separator + 1)..];
            ZipArchiveEntry entry = archive.GetEntry($"{directory}native/{rid}/{fileName}") ?? throw new InvalidDataException($"{jar} lists {fileName} for {rid} but does not hold it.");

            using MemoryStream bytes = new();

            using (Stream stream = entry.Open())
                stream.CopyTo(bytes);

            libraries.Add(new AgentLibrary(rid, fileName, bytes.ToArray()));
        }

        return libraries;
    }

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
            index.Add($"{library.Rid}={library.FileName}\n");
            Add(archive, $"{resources}native/{library.Rid}/{library.FileName}", library.Bytes);
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
            hash.AppendData(Encoding.UTF8.GetBytes(library.Rid + "=" + library.FileName + "\n"));
            hash.AppendData(library.Bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..7];
    }

    private static string? ReadText(ZipArchive archive, string name)
    {
        if (archive.GetEntry(name) is not { } entry)
            return null;

        using StreamReader reader = new(entry.Open(), Encoding.UTF8);

        return reader.ReadToEnd();
    }
}

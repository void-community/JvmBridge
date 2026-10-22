using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

using JvmBridge.Tool.Commands;
using JvmBridge.Launcher;

using Xunit;

namespace JvmBridge.Tool.Tests;

/// <summary>Verifies <c>jvmbridge pack</c>.</summary>
public sealed class PackCommandTests
{
    /// <summary>The launcher class in the jar is a Java 8 class file, so it loads on every JVM.</summary>
    [Fact]
    public void CarriesAJava8Launcher()
    {
        byte[] launcher = LauncherJar.ReadLauncher();

        Assert.Equal(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, launcher[..4]);
        Assert.Equal(expected: 52, (launcher[6] << 8) | launcher[7]);
    }

    /// <summary>Jars of single platforms combine into one, which is what a CI matrix that publishes each platform on its own runner needs; the same library twice counts once.</summary>
    [Fact]
    public void CombinesTheJarsOfSinglePlatforms()
    {
        string folder = ToolFixtures.CreateFolder();

        try
        {
            string linux = Path.Combine(folder, path2: "Agent.so");
            string windows = Path.Combine(folder, path2: "Agent.dll");
            string linuxJar = Path.Combine(folder, path2: "linux.jar");
            string windowsJar = Path.Combine(folder, path2: "windows.jar");
            string combined = Path.Combine(folder, path2: "all.jar");
            string direct = Path.Combine(folder, path2: "direct.jar");

            File.WriteAllText(linux, contents: "linux library");
            File.WriteAllText(windows, contents: "windows library");

            using StringWriter output = new();

            using StringWriter error = new();

            Assert.Equal(expected: 0, PackCommand.Run(["linux-x64=" + linux], [], linuxJar, output, error));
            Assert.Equal(expected: 0, PackCommand.Run(["win-x64=" + windows], [], windowsJar, output, error));
            Assert.Equal(expected: 0, PackCommand.Run(["linux-x64=" + linux], [linuxJar, windowsJar, linuxJar], combined, output, error));
            Assert.Equal(expected: 0, PackCommand.Run(["linux-x64=" + linux, "win-x64=" + windows], [], direct, output, error));

            Assert.Empty(error.ToString());
            Assert.Equal(File.ReadAllBytes(direct), File.ReadAllBytes(combined));
            Assert.Equal(["linux-x64", "win-x64"], LauncherJar.Read(combined).Select(static library => library.Rid));
            Assert.Equal(expected: "windows library", System.Text.Encoding.UTF8.GetString(LauncherJar.Read(combined)[index: 1].Bytes));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Different libraries get different launcher class names, so that two agent jars on one command line do not share the class of the first; the class is otherwise unchanged.</summary>
    [Fact]
    public void NamesTheLauncherAfterTheLibraries()
    {
        string folder = ToolFixtures.CreateFolder();

        try
        {
            string library = Path.Combine(folder, path2: "Agent.so");
            string other = Path.Combine(folder, path2: "Other.so");

            File.WriteAllText(library, contents: "library");
            File.WriteAllText(other, contents: "library");

            using StringWriter output = new();

            using StringWriter error = new();

            Assert.Equal(expected: 0, PackCommand.Run(["linux-x64=" + library], [], Path.Combine(folder, path2: "one.jar"), output, error));
            Assert.Equal(expected: 0, PackCommand.Run(["linux-x64=" + other], [], Path.Combine(folder, path2: "two.jar"), output, error));

            string[] names = [.. Regex.Matches(output.ToString(), pattern: "launcher (jvmbridge\\.L[0-9a-f]{7})").Select(static match => match.Groups[groupnum: 1].Value)];

            Assert.Equal(expected: 2, names.Length);
            Assert.NotEqual(names[0], names[1]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The jar has the manifest that names the launcher, the launcher class, the index and one library per platform; building it twice gives the same bytes.</summary>
    [Fact]
    public void PacksLibrariesForEveryPlatform()
    {
        string folder = ToolFixtures.CreateFolder();

        try
        {
            string linux = Path.Combine(folder, path2: "Agent.so");
            string windows = Path.Combine(folder, path2: "Agent.dll");
            string jar = Path.Combine(folder, path2: "agent.jar");
            string again = Path.Combine(folder, path2: "again.jar");

            File.WriteAllText(linux, contents: "linux library");
            File.WriteAllText(windows, contents: "windows library");

            using StringWriter output = new();

            using StringWriter error = new();

            int status = PackCommand.Run(["win-x64=" + windows, "linux-x64=" + linux], [], jar, output, error);
            int repeated = PackCommand.Run(["linux-x64=" + linux, "win-x64=" + windows], [], again, output, error);

            Assert.Equal(expected: 0, status);
            Assert.Equal(expected: 0, repeated);
            Assert.Empty(error.ToString());
            Assert.Equal(File.ReadAllBytes(jar), File.ReadAllBytes(again));

            using ZipArchive archive = ZipFile.OpenRead(jar);

            string launcher = archive.Entries.Single(
                static entry => entry.FullName.StartsWith(value: "jvmbridge/L", StringComparison.Ordinal) && entry.FullName.EndsWith(value: ".class", StringComparison.Ordinal)
            ).FullName;

            string name = launcher[..^".class".Length];

            Assert.Matches(expectedRegexPattern: "^jvmbridge/L[0-9a-f]{7}\\.class$", launcher);
            Assert.Equal(
                ["META-INF/MANIFEST.MF", launcher, name + "/native/linux-x64/Agent.so", name + "/native/win-x64/Agent.dll", name + "/native.properties"],
                archive.Entries.Select(static entry => entry.FullName)
            );
            Assert.Equal(expected: "linux-x64=Agent.so\nwin-x64=Agent.dll\n", Read(archive, name + "/native.properties"));
            Assert.Equal(expected: "windows library", Read(archive, name + "/native/win-x64/Agent.dll"));
            Assert.Contains(
                $"Premain-Class: {name.Replace(oldChar: '/', newChar: '.')}\r\n",
                Read(archive, name: "META-INF/MANIFEST.MF"),
                StringComparison.Ordinal
            );
            Assert.Contains(
                $"Agent-Class: {name.Replace(oldChar: '/', newChar: '.')}\r\n",
                Read(archive, name: "META-INF/MANIFEST.MF"),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>An argument that is not &lt;rid&gt;=&lt;path&gt;, an unknown platform, a missing file, a repeated platform and no agent at all are refused, and nothing is written.</summary>
    [Fact]
    public void RefusesBadArguments()
    {
        string folder = ToolFixtures.CreateFolder();

        try
        {
            string library = Path.Combine(folder, path2: "Agent.so");
            string jar = Path.Combine(folder, path2: "agent.jar");

            File.WriteAllText(library, contents: "library");

            using StringWriter output = new();

            using StringWriter error = new();

            Assert.Equal(expected: 1, PackCommand.Run([library], [], jar, output, error));
            Assert.Equal(expected: 1, PackCommand.Run(["plan9-x64=" + library], [], jar, output, error));
            Assert.Equal(expected: 1, PackCommand.Run(["linux-x64=" + Path.Combine(folder, path2: "nothing.so")], [], jar, output, error));
            string other = Path.Combine(folder, path2: "Other.so");

            File.WriteAllText(other, contents: "another library");

            Assert.Equal(expected: 1, PackCommand.Run(["linux-x64=" + library, "linux-x64=" + other], [], jar, output, error));
            Assert.Equal(expected: 1, PackCommand.Run([], [], jar, output, error));
            Assert.False(File.Exists(jar));
            Assert.Contains(expectedSubstring: "Known: linux-arm,", error.ToString(), StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "given more than once, with different libraries", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A jar that is not a JvmBridge jar, a missing jar and two jars with different libraries for one platform are refused.</summary>
    [Fact]
    public void RefusesJarsThatDoNotCombine()
    {
        string folder = ToolFixtures.CreateFolder();

        try
        {
            string library = Path.Combine(folder, path2: "Agent.so");
            string other = Path.Combine(folder, path2: "Other.so");
            string first = Path.Combine(folder, path2: "first.jar");
            string second = Path.Combine(folder, path2: "second.jar");
            string plain = Path.Combine(folder, path2: "plain.jar");
            string content = Path.Combine(folder, path2: "content");

            File.WriteAllText(library, contents: "library");
            File.WriteAllText(other, contents: "another library");
            new DirectoryInfo(content).Create();
            File.WriteAllText(Path.Combine(content, path2: "readme.txt"), contents: "not an agent");
            ZipFile.CreateFromDirectory(content, plain);

            using StringWriter output = new();

            using StringWriter error = new();

            Assert.Equal(expected: 0, PackCommand.Run(["linux-x64=" + library], [], first, output, error));
            Assert.Equal(expected: 0, PackCommand.Run(["linux-x64=" + other], [], second, output, error));
            Assert.Equal(expected: 1, PackCommand.Run([], [first, second], Path.Combine(folder, path2: "conflict.jar"), output, error));
            Assert.Equal(expected: 1, PackCommand.Run([], [plain], Path.Combine(folder, path2: "plain-out.jar"), output, error));
            Assert.Equal(
                expected: 1,
                PackCommand.Run([], [Path.Combine(folder, path2: "nothing.jar")], Path.Combine(folder, path2: "missing-out.jar"), output, error)
            );
            Assert.Contains(expectedSubstring: "linux-x64 is given more than once, with different libraries", error.ToString(), StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "has no manifest", error.ToString(), StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "the jar does not exist", error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(expectedSubstring: "(Parameter", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Renaming changes only the constant that holds the class name.</summary>
    [Fact]
    public void RenamesTheLauncherInOneConstant()
    {
        byte[] launcher = LauncherJar.ReadLauncher();

        byte[] renamed = LauncherJar.Rename(launcher, name: "jvmbridge/L1234567");

        Assert.Equal(launcher.Length, renamed.Length);
        Assert.Equal(expected: 7, launcher.Zip(renamed).Count(pair => pair.First != pair.Second));
        ArgumentException failure = Assert.Throws<ArgumentException>(() => LauncherJar.Rename(launcher, name: "jvmbridge/Short"));

        Assert.Equal(expected: "name", failure.ParamName);
    }

    private static string Read(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"The jar has no {name}.");

        using StreamReader reader = new(entry.Open(), Encoding.UTF8);

        return reader.ReadToEnd();
    }
}

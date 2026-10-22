using System.IO.Compression;

using JvmBridge.ClassFiles;

using JvmBridge.Tool.Commands;

using Xunit;

namespace JvmBridge.Tool.Tests;

/// <summary>Verifies <c>jvmbridge dump</c>.</summary>
public sealed class DumpCommandTests
{
    /// <summary>A class file is disassembled like the library does, and the method filter applies.</summary>
    [Fact]
    public void DisassemblesAClassFile()
    {
        using StringWriter output = new();

        using StringWriter error = new();

        int status = DumpCommand.Run(ToolFixtures.Sample, className: null, method: "greeting", output, error);

        Assert.Equal(expected: 0, status);
        Assert.Equal(ClassFileDisassembler.Disassemble(File.ReadAllBytes(ToolFixtures.Sample), method: "greeting"), output.ToString());
        Assert.Contains(expectedSubstring: "greeting()Ljava/lang/String;", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "limit()I", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A jar without --class lists its classes, and with --class dumps one.</summary>
    [Fact]
    public void ReadsClassesFromAJar()
    {
        string folder = ToolFixtures.CreateFolder();

        try
        {
            string jar = Path.Combine(folder, path2: "app.jar");

            ZipFile.CreateFromDirectory(ToolFixtures.ClassFiles, jar);

            using StringWriter listing = new();

            using StringWriter dump = new();

            using StringWriter error = new();

            int listed = DumpCommand.Run(jar, className: null, method: null, listing, error);
            int dumped = DumpCommand.Run(jar, className: "fixture/Sample", method: "limit", dump, error);

            Assert.Equal(expected: 0, listed);
            Assert.Equal(expected: 0, dumped);
            Assert.Equal("fixture/Annotated" + Environment.NewLine + "fixture/Sample" + Environment.NewLine, listing.ToString());
            Assert.Contains(expectedSubstring: "limit()I", dump.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A missing file and a class that is not in the jar are errors that say what to do.</summary>
    [Fact]
    public void ReportsMissingInputs()
    {
        string folder = ToolFixtures.CreateFolder();

        try
        {
            string jar = Path.Combine(folder, path2: "empty.jar");

            string content = Path.Combine(folder, path2: "content");

            new DirectoryInfo(content).Create();
            File.WriteAllText(Path.Combine(content, path2: "readme.txt"), contents: "no classes");
            ZipFile.CreateFromDirectory(content, jar);

            using StringWriter output = new();

            using StringWriter missingFile = new();

            using StringWriter missingClass = new();

            int first = DumpCommand.Run(Path.Combine(folder, path2: "nothing.class"), className: null, method: null, output, missingFile);
            int second = DumpCommand.Run(jar, className: "fixture/Sample", method: null, output, missingClass);

            Assert.Equal(expected: 1, first);
            Assert.Equal(expected: 1, second);
            Assert.Contains(expectedSubstring: "does not exist", missingFile.ToString(), StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "has no class fixture/Sample", missingClass.ToString(), StringComparison.Ordinal);
            Assert.Empty(output.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

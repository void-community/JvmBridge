using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>Verifies the snapshot assertion on disassembled classes.</summary>
[Collection(nameof(SnapshotTests))]
public sealed class SnapshotTests
{
    private static byte[] Subject
    {
        get
        {
            ClassFileWriter writer = new(name: "pkg/Subject");
            int abs = writer.AddMethodReference(owner: "java/lang/Math", name: "abs", descriptor: "(I)I");
            byte[] code = [0x1a, 0xb8, (byte)(abs >> 8), (byte)abs, 0xac];

            return writer.AddMethod(name: "run", descriptor: "(I)I", maxStack: 1, maxLocals: 1, code).ToArray();
        }
    }

    /// <summary>Output that equals the snapshot passes and leaves no received file behind.</summary>
    [Fact]
    public void AcceptsAMatchingSnapshot()
    {
        using TemporaryFolder folder = new();

        string snapshot = Path.Combine(folder.Path, path2: "run.txt");
        File.WriteAllText(snapshot, ClassFileDisassembler.Disassemble(Subject, method: "run"));
        File.WriteAllText(snapshot + ".received", contents: "stale");

        ClassFileAssert.MatchesSnapshot(Subject, snapshot, method: "run");

        Assert.False(File.Exists(snapshot + ".received"));
    }

    /// <summary>A different disassembly fails with the first differing line and writes the received text next to the snapshot.</summary>
    [Fact]
    public void RejectsADifferentDisassembly()
    {
        using TemporaryFolder folder = new();

        string snapshot = Path.Combine(folder.Path, path2: "run.txt");
        File.WriteAllText(snapshot, contents: "  method public static run(I)I\n    max_stack=1 max_locals=1\n      0: aload_0\n");

        Exception? failure = Record.Exception(() => ClassFileAssert.MatchesSnapshot(Subject, snapshot, method: "run"));

        Assert.Contains(expectedSubstring: "line 3 expected \"0: aload_0\" but was \"0: iload_0\"", failure?.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "iload_0", File.ReadAllText(snapshot + ".received"), StringComparison.Ordinal);
    }

    /// <summary>A missing snapshot fails and names the file to copy or the variable that accepts it.</summary>
    [Fact]
    public void RejectsAMissingSnapshot()
    {
        using TemporaryFolder folder = new();

        string snapshot = Path.Combine(folder.Path, path2: "missing.txt");

        Exception? failure = Record.Exception(() => ClassFileAssert.MatchesSnapshot(Subject, snapshot));

        Assert.Contains(expectedSubstring: "There is no snapshot", failure?.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "JVMBRIDGE_UPDATE_SNAPSHOTS=1", failure?.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(snapshot + ".received"));
    }

    /// <summary>A relative path is found in the test project's directory, where the checked-in snapshot is.</summary>
    [Fact]
    public void ResolvesRelativePathsInTheTestProject()
    {
        ClassFileAssert.MatchesSnapshot(Subject, snapshot: "Snapshots/Subject.run.txt", method: "run");
    }

    /// <summary>With JVMBRIDGE_UPDATE_SNAPSHOTS=1 a run writes the snapshot instead of failing.</summary>
    [Fact]
    public void UpdateModeWritesTheSnapshot()
    {
        using TemporaryFolder folder = new();

        string snapshot = Path.Combine(folder.Path, path2: "nested", path3: "run.txt");
        string? previous = Environment.GetEnvironmentVariable(variable: "JVMBRIDGE_UPDATE_SNAPSHOTS");

        try
        {
            Environment.SetEnvironmentVariable(variable: "JVMBRIDGE_UPDATE_SNAPSHOTS", value: "1");

            ClassFileAssert.MatchesSnapshot(Subject, snapshot, method: "run");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable: "JVMBRIDGE_UPDATE_SNAPSHOTS", previous);
        }

        Assert.Equal(ClassFileDisassembler.Disassemble(Subject, method: "run"), File.ReadAllText(snapshot));
    }

    private sealed class TemporaryFolder : IDisposable
    {
        internal TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jvmbridge-snapshots-" + Guid.NewGuid().ToString(format: "N"));
            Directory.CreateDirectory(Path).Refresh();
        }

        internal string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

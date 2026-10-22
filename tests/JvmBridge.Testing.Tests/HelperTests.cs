using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>Verifies source globbing, published-agent lookup and class-file assertions.</summary>
public sealed class HelperTests
{
    /// <summary>Calls and DoesNotCall read the invocations of a method from the class bytes.</summary>
    [Fact]
    public void AssertsOnTheCallsOfAMethod()
    {
        ClassFileWriter writer = new(name: "pkg/Subject");
        int abs = writer.AddMethodReference(owner: "java/lang/Math", name: "abs", descriptor: "(I)I");
        byte[] code = [0x1a, 0xb8, (byte)(abs >> 8), (byte)abs, 0xac];
        byte[] bytes = writer.AddMethod(name: "run", descriptor: "(I)I", maxStack: 1, maxLocals: 1, code).ToArray();

        ClassFileAssert.Calls(bytes, method: "run", owner: "java/lang/Math", name: "abs");
        ClassFileAssert.DoesNotCall(bytes, method: "run", owner: "java/util/Collections", name: "sort");
        Exception? missing = Record.Exception(() => ClassFileAssert.Calls(bytes, method: "run", owner: "java/util/Collections", name: "sort"));
        Exception? unexpected = Record.Exception(() => ClassFileAssert.DoesNotCall(bytes, method: "run", owner: "java/lang/Math", name: "abs"));

        Assert.Contains(expectedSubstring: "does not call java/util/Collections.sort", missing?.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "still calls java/lang/Math.abs", unexpected?.Message, StringComparison.Ordinal);
    }

    /// <summary>A descriptor makes the whole member reference match, so a call of the same name with another descriptor does not satisfy it.</summary>
    [Fact]
    public void AssertsOnTheDescriptorOfACall()
    {
        ClassFileWriter writer = new(name: "pkg/Subject");
        int integerAbs = writer.AddMethodReference(owner: "java/lang/Math", name: "abs", descriptor: "(I)I");
        int longAbs = writer.AddMethodReference(owner: "java/lang/Math", name: "abs", descriptor: "(J)J");

        // iload_0; invokestatic abs(I)I; pop; lconst_0; invokestatic abs(J)J; pop2; return
        byte[] code = [0x1a, 0xb8, (byte)(integerAbs >> 8), (byte)integerAbs, 0x57, 0x09, 0xb8, (byte)(longAbs >> 8), (byte)longAbs, 0x58, 0xb1];
        byte[] bytes = writer.AddMethod(name: "run", descriptor: "(I)V", maxStack: 2, maxLocals: 1, code).ToArray();

        ClassFileAssert.Calls(bytes, method: "run", owner: "java/lang/Math", name: "abs", descriptor: "(I)I");
        ClassFileAssert.Calls(bytes, method: "run", owner: "java/lang/Math", name: "abs", descriptor: "(J)J");
        ClassFileAssert.DoesNotCall(bytes, method: "run", owner: "java/lang/Math", name: "abs", descriptor: "(D)D");
        Exception? wrong = Record.Exception(() => ClassFileAssert.Calls(bytes, method: "run", owner: "java/lang/Math", name: "abs", descriptor: "(F)F"));
        Exception? present = Record.Exception(() => ClassFileAssert.DoesNotCall(bytes, method: "run", owner: "java/lang/Math", name: "abs", descriptor: "(J)J"));

        Assert.Contains(expectedSubstring: "does not call java/lang/Math.abs(F)F", wrong?.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "java/lang/Math.abs(I)I, java/lang/Math.abs(J)J", wrong?.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "still calls java/lang/Math.abs(J)J", present?.Message, StringComparison.Ordinal);
    }

    /// <summary>The reason a test is skipped for an unpublished agent names the agent and says how to publish it.</summary>
    [Fact]
    public void ExplainsHowToPublishAnAgent()
    {
        string message = PublishedAgent.MissingMessage(name: "Sample", identifier: "linux-arm64");

        Assert.Equal(
            expected: "The Sample agent is not published for linux-arm64. Publish it with: dotnet publish -c Release -r linux-arm64 -o artifacts/agent/linux-arm64",
            message
        );
    }

    /// <summary>A published agent is found by walking up from a start directory to artifacts/agent/&lt;rid&gt;.</summary>
    [Fact]
    public void FindsAPublishedAgentByWalkingUp()
    {
        string root = Directory.CreateTempSubdirectory(prefix: "jvmbridge-agent-lookup-").FullName;

        try
        {
            string file = "Sample" + (OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so");
            DirectoryInfo published = Directory.CreateDirectory(Path.Combine(root, path2: "artifacts", path3: "agent", path4: "linux-x64"));
            DirectoryInfo nested = Directory.CreateDirectory(Path.Combine(paths: [root, "tests", "Unit", "bin", "Release"]));
            File.WriteAllText(Path.Combine(published.FullName, file), contents: "agent");

            PublishedAgent? found = PublishedAgent.TryFind(name: "Sample", identifier: "linux-x64", [nested.FullName]);
            PublishedAgent? otherPlatform = PublishedAgent.TryFind(name: "Sample", identifier: "osx-arm64", [nested.FullName]);
            PublishedAgent? otherName = PublishedAgent.TryFind(name: "Missing", identifier: "linux-x64", [nested.FullName]);

            Assert.Equal(Path.Combine(published.FullName, file), found?.FilePath);
            Assert.Null(otherPlatform);
            Assert.Null(otherName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A wildcard pattern finds files recursively when it has a double-star segment, and only the top directory otherwise.</summary>
    [Fact]
    public void GlobsRecursivelyOnlyWithADoubleStar()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path2: "Fixtures"));

        string[] recursive = SourceGlob.Resolve(pattern: "Fixtures/**/*.java", [AppContext.BaseDirectory]);
        string[] topLevel = SourceGlob.Resolve(pattern: "Fixtures/Good/*.java", [AppContext.BaseDirectory]);
        string[] directory = SourceGlob.Resolve(pattern: "Fixtures/Good", [AppContext.BaseDirectory]);
        string[] single = SourceGlob.Resolve(pattern: "Fixtures/Broken/Broken.java", [AppContext.BaseDirectory]);

        Assert.Equal(expected: 2, recursive.Length);
        Assert.All(recursive, path => Assert.StartsWith(root, path, StringComparison.Ordinal));
        Assert.Equal(Path.Combine(root, path2: "Good", path3: "Greeter.java"), Assert.Single(topLevel));
        Assert.Equal(topLevel, directory);
        Assert.Equal(Path.Combine(root, path2: "Broken", path3: "Broken.java"), Assert.Single(single));
    }

    /// <summary>A pattern that matches nothing says where it looked.</summary>
    [Fact]
    public void ReportsPatternsThatMatchNothing()
    {
        FileNotFoundException failure = Assert.Throws<FileNotFoundException>(static () => SourceGlob.Resolve(pattern: "Fixtures/Absent/**/*.java", [AppContext.BaseDirectory]));

        Assert.Contains(expectedSubstring: "Fixtures/Absent/**/*.java", failure.Message, StringComparison.Ordinal);
    }
}

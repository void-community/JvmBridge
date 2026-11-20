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
        byte[] code = [0x1a, 0xb8, byte.CreateTruncating(abs >> 8), byte.CreateTruncating(abs), 0xac];
        byte[] bytes = writer.AddMethod(name: "run", descriptor: "(I)I", maxStack: 1, maxLocals: 1, code).ToArray();

        ClassFileAssert.Calls(bytes, method: "run", owner: "java/lang/Math", name: "abs");
        ClassFileAssert.DoesNotCall(bytes, method: "run", owner: "java/util/Collections", name: "sort");

        Exception? missing = Record.Exception(() => ClassFileAssert.Calls(bytes,
            method: "run",
            owner: "java/util/Collections",
            name: "sort"));

        Exception? unexpected = Record.Exception(() => ClassFileAssert.DoesNotCall(bytes,
            method: "run",
            owner: "java/lang/Math",
            name: "abs"));

        Assert.Contains(expectedSubstring: "does not call java/util/Collections.sort",
            missing?.Message,
            StringComparison.Ordinal);

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
        byte[] code =
        [
            0x1a,
            0xb8,
            byte.CreateTruncating(integerAbs >> 8),
            byte.CreateTruncating(integerAbs),
            0x57,
            0x09,
            0xb8,
            byte.CreateTruncating(longAbs >> 8),
            byte.CreateTruncating(longAbs),
            0x58,
            0xb1,
        ];

        byte[] bytes = writer.AddMethod(name: "run", descriptor: "(I)V", maxStack: 2, maxLocals: 1, code).ToArray();

        ClassFileAssert.Calls(bytes, method: "run", owner: "java/lang/Math", name: "abs", descriptor: "(I)I");
        ClassFileAssert.Calls(bytes, method: "run", owner: "java/lang/Math", name: "abs", descriptor: "(J)J");
        ClassFileAssert.DoesNotCall(bytes, method: "run", owner: "java/lang/Math", name: "abs", descriptor: "(D)D");

        Exception? wrong = Record.Exception(() => ClassFileAssert.Calls(bytes,
            method: "run",
            owner: "java/lang/Math",
            name: "abs",
            descriptor: "(F)F"));

        Exception? present = Record.Exception(() => ClassFileAssert.DoesNotCall(bytes,
            method: "run",
            owner: "java/lang/Math",
            name: "abs",
            descriptor: "(J)J"));

        Assert.Contains(expectedSubstring: "does not call java/lang/Math.abs(F)F", wrong?.Message, StringComparison.Ordinal);

        Assert.Contains(expectedSubstring: "java/lang/Math.abs(I)I, java/lang/Math.abs(J)J",
            wrong?.Message,
            StringComparison.Ordinal);

        Assert.Contains(expectedSubstring: "still calls java/lang/Math.abs(J)J", present?.Message, StringComparison.Ordinal);
    }

    /// <summary>The reason a test is skipped for an unpublished jar says to publish with JvmBridgeAgentJar.</summary>
    [Fact]
    public void ExplainsHowToPublishAJar()
    {
        string message = PublishedAgent.MissingJarMessage(name: "Sample", identifier: "linux-arm64");

        Assert.Equal(expected: "The Sample agent jar is not published for linux-arm64. Publish it with: dotnet publish -c Release -r linux-arm64 -o artifacts/agent/linux-arm64 -p:JvmBridgeAgentJar=true", message);
    }

    /// <summary>The reason a test is skipped for an unpublished agent names the agent and says how to publish it.</summary>
    [Fact]
    public void ExplainsHowToPublishAnAgent()
    {
        string message = PublishedAgent.MissingMessage(name: "Sample", identifier: "linux-arm64");

        Assert.Equal(expected: "The Sample agent is not published for linux-arm64. Publish it with: dotnet publish -c Release -r linux-arm64 -o artifacts/agent/linux-arm64", message);
    }

    /// <summary>A published agent is found by walking up from a start directory to artifacts/agent/&lt;rid&gt;.</summary>
    [Fact]
    public void FindsAPublishedAgentByWalkingUp()
    {
        string root = Directory.CreateTempSubdirectory(prefix: "jvmbridge-agent-lookup-").FullName;

        try
        {
            string file = "Sample" + (OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so");

            DirectoryInfo published = Directory.CreateDirectory(Path.Combine(root,
                path2: "artifacts",
                path3: "agent",
                path4: "linux-x64"));

            DirectoryInfo nested = Directory.CreateDirectory(Path.Combine(paths: [root, "tests", "Unit", "bin", "Release"]));

            File.WriteAllText(Path.Combine(published.FullName, file), contents: "agent");

            PublishedAgent? found = PublishedAgent.TryFind(name: "Sample", identifier: "linux-x64", starts: [nested.FullName]);

            PublishedAgent? otherPlatform = PublishedAgent.TryFind(name: "Sample",
                identifier: "osx-arm64",
                starts: [nested.FullName]);

            PublishedAgent? otherName = PublishedAgent.TryFind(name: "Missing",
                identifier: "linux-x64",
                starts: [nested.FullName]);

            Assert.Equal(Path.Combine(published.FullName, file), found?.FilePath);
            Assert.Null(otherPlatform);
            Assert.Null(otherName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A jar beside the published library is found as a jar, and loaded with -javaagent; a library is loaded with -agentpath; the options follow the = either way.</summary>
    [Fact]
    public void FindsAndLoadsAPublishedJar()
    {
        string root = Directory.CreateTempSubdirectory(prefix: "jvmbridge-jar-lookup-").FullName;

        try
        {
            DirectoryInfo published = Directory.CreateDirectory(Path.Combine(root,
                path2: "artifacts",
                path3: "agent",
                path4: "linux-x64"));

            string jar = Path.Combine(published.FullName, path2: "Sample.jar");

            File.WriteAllText(jar, contents: "jar");

            PublishedAgent? found = PublishedAgent.TryFind(name: "Sample.jar", identifier: "linux-x64", starts: [root]);
            PublishedAgent? library = PublishedAgent.TryFind(name: "Sample", identifier: "linux-x64", starts: [root]);
            PublishedAgent native = new(FilePath: "/agents/Sample.so");

            // The jar is found under its own name only, and its extension is what tells it from a library.
            Assert.Equal(jar, found?.FilePath);
            Assert.True(found?.IsJar);
            Assert.Null(library);
            Assert.False(native.IsJar);

            // A jar is loaded with -javaagent and a library with -agentpath, and the options are left out when there are none.
            Assert.Equal("-javaagent:" + jar + "=log=debug", Jdk.AgentArgument(found ?? native, options: "log=debug"));
            Assert.Equal(expected: "-agentpath:/agents/Sample.so", Jdk.AgentArgument(native, options: null));
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

        // The same fixture tree, asked for with a double star, with a single star, as a directory and as one file.
        string[] recursive = SourceGlob.Resolve(pattern: "Fixtures/**/*.java", roots: [AppContext.BaseDirectory]);
        string[] topLevel = SourceGlob.Resolve(pattern: "Fixtures/Good/*.java", roots: [AppContext.BaseDirectory]);
        string[] directory = SourceGlob.Resolve(pattern: "Fixtures/Good", roots: [AppContext.BaseDirectory]);
        string[] single = SourceGlob.Resolve(pattern: "Fixtures/Broken/Broken.java", roots: [AppContext.BaseDirectory]);

        // The double star reaches the Java files of every directory under the fixtures.
        Assert.Equal(expected: 3, recursive.Length);
        Assert.All(recursive, path => Assert.StartsWith(root, path, StringComparison.Ordinal));

        // A single star stays in the directory it names, a directory stands for its own files, and a file matches itself.
        Assert.Equal(Path.Combine(root, path2: "Good", path3: "Greeter.java"), Assert.Single(topLevel));
        Assert.Equal(topLevel, directory);
        Assert.Equal(Path.Combine(root, path2: "Broken", path3: "Broken.java"), Assert.Single(single));
    }

    /// <summary>A pattern that matches nothing says where it looked.</summary>
    [Fact]
    public void ReportsPatternsThatMatchNothing()
    {
        FileNotFoundException failure = Assert.Throws<FileNotFoundException>(static () => SourceGlob.Resolve(pattern: "Fixtures/Absent/**/*.java", roots: [AppContext.BaseDirectory]));

        Assert.Contains(expectedSubstring: "Fixtures/Absent/**/*.java", failure.Message, StringComparison.Ordinal);
    }
}

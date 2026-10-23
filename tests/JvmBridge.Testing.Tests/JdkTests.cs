using System.Globalization;
using System.Reflection;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;
using JvmBridge.ClassFiles;

using Xunit;
using Xunit.Sdk;

namespace JvmBridge.Testing.Tests;

/// <summary>Verifies compiling and running Java with the pinned JDKs.</summary>
public sealed class JdkTests
{
    /// <summary>A variable that names a directory that does not exist fails the test, instead of the pinned JDK being downloaded in its place.</summary>
    [Fact]
    public async Task AStaleVariableFailsInsteadOfFallingBackAsync()
    {
        Environment.SetEnvironmentVariable(variable: "JVMBRIDGE_JDK_98", value: "/nonexistent/jdk-98");

        try
        {
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(static () => new Jdk(major: 98).GetHomeAsync(TestContext.Current.CancellationToken));

            Assert.StartsWith(
                expectedStartString: "JVMBRIDGE_JDK_98 names /nonexistent/jdk-98, which does not exist.",
                failure.Message,
                StringComparison.Ordinal
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable: "JVMBRIDGE_JDK_98", value: null);
        }
    }

    /// <summary>The pinned Java 6 and 7 JDKs compile for their own release with -source and -target, and run the class.</summary>
    [Theory]
    [JdkData(6, 7)]
    public async Task CompilesAndRunsOnJava6And7Async(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classPath = await jdk.CompileAsync(sources: "Fixtures/Legacy/**/*.java", jdk.Major, TestContext.Current.CancellationToken);
        JavaRunOptions options = new();
        options.Arguments.Add(item: "exit4");

        JavaRun run = await jdk.RunAsync(mainClass: "fixture.Greeter", classPath, options: options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(44 + jdk.Major, MajorVersion(classPath));
        Assert.Equal(expected: 4, run.ExitCode);
        Assert.Contains("hello 1." + jdk.Major.ToString(CultureInfo.InvariantCulture), run.Output, StringComparison.Ordinal);
    }

    /// <summary>Classes are compiled for Java 8 unless another release is requested, even by a newer JDK.</summary>
    [Theory]
    [JdkData(17)]
    public async Task CompilesForJava8ByDefaultAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string defaultPath = await jdk.CompileAsync(sources: "Fixtures/Good/**/*.java", cancellationToken: TestContext.Current.CancellationToken);
        string newerPath = await jdk.CompileAsync(sources: "Fixtures/Good/**/*.java", release: 17, TestContext.Current.CancellationToken);

        Assert.Equal(expected: 52, MajorVersion(defaultPath));
        Assert.Equal(expected: 61, MajorVersion(newerPath));
    }

    /// <summary>Sources compile and the resulting class runs, with arguments passed through and the exit code reported.</summary>
    [Theory]
    [JdkData(17)]
    public async Task CompilesRunsAndReportsTheExitCodeAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classPath = await jdk.CompileAsync(sources: "Fixtures/Good/**/*.java", cancellationToken: TestContext.Current.CancellationToken);
        JavaRunOptions options = new();
        options.Arguments.Add(item: "exit3");

        JavaRun run = await jdk.RunAsync(mainClass: "fixture.Greeter", classPath, options: options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected: 3, run.ExitCode);
        Assert.Contains(expectedSubstring: "hello exit3", run.Output, StringComparison.Ordinal);
    }

    /// <summary>The reason a test is skipped names the JDK, the platform, the inventory's reason and the variable that overrides the pin.</summary>
    [Fact]
    public void ExplainsWhyAJdkIsUnavailable()
    {
        Jdk jdk = new(major: 99);

        string message = jdk.UnavailableMessage(runtimeIdentifier: "linux-x64", reason: "archive not published", installedVariable: "JVMBRIDGE_JDK_99");

        Assert.Equal(
            expected: "No pinned Java 99 (hotspot) for linux-x64: archive not published. Set JVMBRIDGE_JDK_99 to use an installed JDK.",
            message
        );
    }

    /// <summary>The Java 6 and 7 JDKs come from the legacy inventory that the package embeds beside the main one, pinned by checksum, and a platform Azul has no build for has none.</summary>
    [Fact]
    public void FindsTheLegacyJdks()
    {
        foreach (int major in new[] { 6, 7 })
        {
            JdkEntry? entry = PinnedJdk.Find(major, implementation: "hotspot", runtimeIdentifier: "linux-x64");

            Assert.NotNull(entry);
            Assert.Equal(expected: "available", entry.Status);
            Assert.Equal(expected: "zulu", entry.Distribution);
            Assert.Equal(expected: 64, entry.Sha256?.Length);
        }

        Assert.Equal(expected: "unavailable", PinnedJdk.Find(major: 7, implementation: "hotspot", runtimeIdentifier: "linux-arm64")?.Status);
        Assert.Equal(expected: "unavailable", PinnedJdk.Find(major: 6, implementation: "hotspot", runtimeIdentifier: "osx-x64")?.Status);
    }

    /// <summary>An installed JDK is identified from its properties, which Java 8 reports as 1.8, through the noise of JAVA_TOOL_OPTIONS; output without them identifies nothing.</summary>
    [Fact]
    public void IdentifiesInstalledJdksFromTheirProperties()
    {
        const string java8 = "Picked up JAVA_TOOL_OPTIONS: -Dhttp.proxyHost=proxy\nProperty settings:\n    java.specification.version = 1.8\n    java.vm.name = OpenJDK 64-Bit Server VM\nopenjdk version \"1.8.0_504\"\n";
        const string java25 = "Property settings:\r\n    java.specification.version = 25\r\n    java.vm.name = OpenJDK 64-Bit Server VM\r\nopenjdk version \"25.0.4.1\" 2026-08-18\r\n";
        const string openJ9 = "Property settings:\n    java.specification.version = 17\n    java.vm.name = Eclipse OpenJ9 VM\n";

        Assert.Equal(new InstalledJdk(Major: 8, Implementation: "hotspot"), InstalledJdk.Parse(java8));
        Assert.Equal(new InstalledJdk(Major: 25, Implementation: "hotspot"), InstalledJdk.Parse(java25));
        Assert.Equal(new InstalledJdk(Major: 17, Implementation: "openj9"), InstalledJdk.Parse(openJ9));
        Assert.Null(InstalledJdk.Parse(output: "Error: could not create the Java Virtual Machine."));
        Assert.Null(InstalledJdk.Parse(output: "    java.specification.version = soon\n    java.vm.name = Some VM\n"));
    }

    /// <summary>A JDK older than Java 6 is rejected up front.</summary>
    [Fact]
    public void RejectsJdksBeforeJava6()
    {
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(static () => new Jdk(major: 5));

        Assert.Equal(expected: "major", failure.ParamName);
    }

    /// <summary>Compiler errors fail the compilation and carry the compiler's message.</summary>
    [Theory]
    [JdkData(17)]
    public async Task ReportsCompilerErrorsAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => jdk.CompileAsync(sources: "Fixtures/Broken/*.java", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(expectedSubstring: "javac failed", failure.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "incompatible types", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A run that does not finish in time is stopped and reported with its output so far.</summary>
    [Theory]
    [JdkData(17)]
    public async Task StopsRunsThatTakeTooLongAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classPath = await jdk.CompileAsync(sources: "Fixtures/Good/**/*.java", cancellationToken: TestContext.Current.CancellationToken);
        JavaRunOptions options = new() { Timeout = TimeSpan.FromSeconds(seconds: 3) };
        options.Arguments.Add(item: "sleep");

        TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(
            () => jdk.RunAsync(mainClass: "fixture.Greeter", classPath, options: options, cancellationToken: TestContext.Current.CancellationToken)
        );

        Assert.Contains(expectedSubstring: "hello sleep", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The data attribute supplies one lazily resolved JDK per requested version.</summary>
    [Fact]
    public async Task TheDataAttributeSuppliesOneJdkPerVersionAsync()
    {
        JdkDataAttribute attribute = new(major: 8, additionalMajors: [17, 21]) { Implementation = "openj9" };
        DisposalTracker tracker = new();

        try
        {
            MethodInfo method = typeof(JdkTests).GetMethod(nameof(ExplainsWhyAJdkIsUnavailable)) ?? throw new InvalidOperationException(message: "Method missing.");
            IReadOnlyCollection<ITheoryDataRow> rows = await attribute.GetData(method, tracker);

            Assert.Equal(["Java 8 (openj9)", "Java 17 (openj9)", "Java 21 (openj9)"], rows.Select(static row => row.TestDisplayName));
        }
        finally
        {
            await tracker.DisposeAsync();
        }
    }

    /// <summary>
    /// A JDK that a variable names is asked what it is. The one the test asked for passes; another major version, another implementation, a directory that does not exist and a directory that is not a JDK fail with the variable, what it named
    /// and what the test needs.
    /// </summary>
    [Theory]
    [JdkData(17)]
    public async Task VerifiesTheJdkThatAVariableNamesAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string home = await jdk.GetHomeAsync(TestContext.Current.CancellationToken);
        string empty = Directory.CreateTempSubdirectory(prefix: "jvmbridge-not-a-jdk-").FullName;

        try
        {
            await new Jdk(major: 17).VerifyInstalledAsync(variable: "JVMBRIDGE_JDK_17", home);

            InvalidOperationException major = await Assert.ThrowsAsync<InvalidOperationException>(() => new Jdk(major: 8).VerifyInstalledAsync(variable: "JVMBRIDGE_JDK_8", home));
            InvalidOperationException implementation = await Assert.ThrowsAsync<InvalidOperationException>(() => new Jdk(major: 17, implementation: "openj9").VerifyInstalledAsync(variable: "JVMBRIDGE_JDK_17", home));
            InvalidOperationException missing = await Assert.ThrowsAsync<InvalidOperationException>(() => new Jdk(major: 8).VerifyInstalledAsync(variable: "JVMBRIDGE_JDK_8", Path.Combine(empty, path2: "typo")));
            InvalidOperationException notJdk = await Assert.ThrowsAsync<InvalidOperationException>(() => new Jdk(major: 8).VerifyInstalledAsync(variable: "JVMBRIDGE_JDK_8", empty));

            Assert.Equal(
                $"JVMBRIDGE_JDK_8 names {home}, which is Java 17 (hotspot), but this test needs Java 8 (hotspot). Point JVMBRIDGE_JDK_8 at a Java 8 (hotspot) JDK, or unset it to use the pinned one.",
                major.Message
            );
            Assert.Contains(
                expectedSubstring: "which is Java 17 (hotspot), but this test needs Java 17 (openj9)",
                implementation.Message,
                StringComparison.Ordinal
            );
            Assert.Contains(expectedSubstring: "set JVMBRIDGE_JDK_17_OPENJ9 for this one", implementation.Message, StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "typo, which does not exist", missing.Message, StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "so it is not a JDK", notJdk.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    private static int MajorVersion(string classPath)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(classPath, path2: "fixture", path3: "Greeter.class"));

        return JavaClassFile.Read(bytes).MajorVersion;
    }
}

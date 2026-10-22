using System.Reflection;

using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>Verifies compiling and running Java with the pinned JDKs.</summary>
public sealed class JdkTests
{
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

    /// <summary>A JDK older than Java 8 is rejected up front.</summary>
    [Fact]
    public void RejectsJdksBeforeJava8()
    {
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(static () => new Jdk(major: 7));

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
        Xunit.Sdk.DisposalTracker tracker = new();

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

    private static int MajorVersion(string classPath)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(classPath, path2: "fixture", path3: "Greeter.class"));

        return JavaClassFile.Read(bytes).MajorVersion;
    }
}

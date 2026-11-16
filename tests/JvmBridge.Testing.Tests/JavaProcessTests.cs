using System.Diagnostics;
using System.Globalization;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>Verifies running a JVM in a working directory, and driving one that keeps running through its output and input, as a server test does.</summary>
public sealed class JavaProcessTests
{
    private const string Main = "fixture.Console";

    /// <summary>Disposing a program that still runs ends it and the JVM it started.</summary>
    /// <param name="jdk">The JDK that compiles the fixture and runs it; Process.pid(), which the fixture prints its child's id with, is Java 9 and later.</param>
    [Theory]
    [JdkData(17)]
    public async Task DisposingEndsTheProgramAndItsChildrenAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string classes = await jdk.CompileAsync(sources: "ProcessFixtures/fixture/*.java", cancellationToken: cancellationToken);

        try
        {
            JavaProcess process = await jdk.StartAsync(Main, classes, cancellationToken: cancellationToken);
            int parent;
            int child;

            await using (process)
            {
                parent = int.Parse(Line(await process.WaitForOutputAsync(text: "ready", cancellationToken), prefix: "PID="), CultureInfo.InvariantCulture);
                await process.WriteLineAsync(line: "child", cancellationToken);
                child = int.Parse(Line(await process.WaitForOutputAsync(text: "CHILD=", cancellationToken), prefix: "CHILD="), CultureInfo.InvariantCulture);

                Assert.False(await EndsAsync(child, TimeSpan.FromSeconds(seconds: 1)), userMessage: "The child JVM did not start.");
            }

            Assert.True(await EndsAsync(parent, TimeSpan.FromSeconds(seconds: 30)), userMessage: "The program still runs.");
            Assert.True(await EndsAsync(child, TimeSpan.FromSeconds(seconds: 30)), userMessage: "The JVM the program started still runs.");
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    /// <summary>
    /// A wait returns for a line that arrived before it and one that arrives after it, the program reads the lines written to its input and ends on the one it waits for, and a wait for a line that never comes fails with the output once the
    /// program has ended. Disposing an ended program does nothing.
    /// </summary>
    /// <param name="jdk">The JDK that compiles the fixture and runs it.</param>
    [Theory]
    [JdkData(8, 17)]
    public async Task DrivesAProgramThroughItsInputAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string classes = await jdk.CompileAsync(sources: "ProcessFixtures/fixture/*.java", cancellationToken: cancellationToken);

        try
        {
            JavaProcess process = await jdk.StartAsync(Main, classes, cancellationToken: cancellationToken);

            await using (process)
            {
                string ready = await process.WaitForOutputAsync(text: "ready", cancellationToken);
                string earlier = await process.WaitForOutputAsync(text: "first", cancellationToken);
                await process.WriteLineAsync(line: "hello", cancellationToken);
                string echoed = await process.WaitForOutputAsync(text: "echo: hello", cancellationToken);
                await process.WriteLineAsync(line: "stop", cancellationToken);
                JavaRun run = await process.WaitForExitAsync(cancellationToken);
                InvalidOperationException never = await Assert.ThrowsAsync<InvalidOperationException>(() => process.WaitForOutputAsync(text: "never", cancellationToken));

                Assert.EndsWith(expectedEndString: "ready", ready, StringComparison.Ordinal);
                Assert.EndsWith(expectedEndString: "first", earlier, StringComparison.Ordinal);
                Assert.EndsWith(expectedEndString: "echo: hello", echoed, StringComparison.Ordinal);
                Assert.Equal(expected: 0, run.ExitCode);
                Assert.Contains(expectedSubstring: "stopping", run.Output, StringComparison.Ordinal);
                Assert.Contains(expectedSubstring: "ended without writing \"never\"", never.Message, StringComparison.Ordinal);
                Assert.Contains(expectedSubstring: "stopping", never.Message, StringComparison.Ordinal);
            }

            await process.DisposeAsync();
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    /// <summary>A cancelled wait fails with the output so far and leaves the program running; when the time limit runs out, the program is ended and its waits fail with the output.</summary>
    /// <param name="jdk">The JDK that compiles the fixture and runs it.</param>
    [Theory]
    [JdkData(8, 17)]
    public async Task EndsAWaitOnCancellationAndTheTimeLimitAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string classes = await jdk.CompileAsync(sources: "ProcessFixtures/fixture/*.java", cancellationToken: cancellationToken);

        try
        {
            JavaProcess running = await jdk.StartAsync(Main, classes, cancellationToken: cancellationToken);

            await using (running)
            {
                string ready = await running.WaitForOutputAsync(text: "ready", cancellationToken);

                using (CancellationTokenSource shortly = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    shortly.CancelAfter(TimeSpan.FromMilliseconds(milliseconds: 300));
                    OperationCanceledException cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitForOutputAsync(text: "never", shortly.Token));

                    Assert.Contains(ready, cancelled.Message, StringComparison.Ordinal);
                }

                await running.WriteLineAsync(line: "still", cancellationToken);
                string still = await running.WaitForOutputAsync(text: "echo: still", cancellationToken);

                Assert.EndsWith(expectedEndString: "echo: still", still, StringComparison.Ordinal);
            }

            JavaProcess limited = await jdk.StartAsync(Main, classes, options: new JavaRunOptions { Timeout = TimeSpan.FromSeconds(seconds: 10) }, cancellationToken: cancellationToken);

            await using (limited)
            {
                string started = await limited.WaitForOutputAsync(text: "ready", cancellationToken);
                TimeoutException late = await Assert.ThrowsAsync<TimeoutException>(() => limited.WaitForOutputAsync(text: "never", cancellationToken));
                TimeoutException ended = await Assert.ThrowsAsync<TimeoutException>(() => limited.WaitForExitAsync(cancellationToken));

                Assert.Contains(expectedSubstring: "did not write \"never\" within 10 seconds", late.Message, StringComparison.Ordinal);
                Assert.Contains(started, late.Message, StringComparison.Ordinal);
                Assert.Contains(expectedSubstring: "did not finish within 10 seconds", ended.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    /// <summary>Megabytes written to both streams arrive whole, each stream's lines in the order the program wrote them.</summary>
    /// <param name="jdk">The JDK that compiles the fixture and runs it.</param>
    [Theory]
    [JdkData(17)]
    public async Task KeepsTheOrderOfEachStreamAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string classes = await jdk.CompileAsync(sources: "ProcessFixtures/fixture/*.java", cancellationToken: cancellationToken);

        try
        {
            JavaProcess process = await jdk.StartAsync(Main, classes, options: new JavaRunOptions { Arguments = ["flood", "60000"] }, cancellationToken: cancellationToken);
            JavaRun run;

            await using (process)
            {
                string flooded = await process.WaitForOutputAsync(text: "flood done", cancellationToken);
                run = await process.WaitForExitAsync(cancellationToken);

                Assert.EndsWith(expectedEndString: "flood done", flooded, StringComparison.Ordinal);
            }

            string[] lines = [.. run.Output.Split(separator: '\n').Select(static line => line.TrimEnd(trimChar: '\r'))];
            int[] written = [.. lines.Where(static line => line.StartsWith(value: "out ", StringComparison.Ordinal)).Select(static line => Number(line))];
            int[] errors = [.. lines.Where(static line => line.StartsWith(value: "err ", StringComparison.Ordinal)).Select(static line => Number(line))];

            Assert.True(run.Output.Length > 4_000_000, userMessage: "The program wrote less than expected.");
            Assert.Equal(Enumerable.Range(start: 0, count: 30000).Select(static index => index * 2), written);
            Assert.Equal(Enumerable.Range(start: 0, count: 30000).Select(static index => (index * 2) + 1), errors);
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    /// <summary>A program run or started in a working directory sees it as user.dir and reads a file there by a relative path.</summary>
    /// <param name="jdk">The JDK that compiles the fixture and runs it.</param>
    [Theory]
    [JdkData(8, 17)]
    public async Task RunsInTheWorkingDirectoryAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string classes = await jdk.CompileAsync(sources: "ProcessFixtures/fixture/*.java", cancellationToken: cancellationToken);
        string directory = Directory.CreateTempSubdirectory(prefix: "jvmbridge-working-").FullName;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, path2: "relative.txt"), contents: "here", cancellationToken);
            JavaRunOptions options = new() { WorkingDirectory = directory, Arguments = ["where"] };

            JavaRun run = await jdk.RunAsync(Main, classes, options: options, cancellationToken: cancellationToken);
            JavaProcess process = await jdk.StartAsync(Main, classes, options: options, cancellationToken: cancellationToken);
            JavaRun started;

            await using (process)
                started = await process.WaitForExitAsync(cancellationToken);

            foreach (JavaRun result in new[] { run, started })
            {
                Assert.Equal(expected: 0, result.ExitCode);
                Assert.Contains(expectedSubstring: "FILE=here", result.Output, StringComparison.Ordinal);

                // macOS reports the temporary directory through /private, so only the unique name is compared.
                Assert.EndsWith(Path.GetFileName(directory), Line(result.Output, prefix: "DIR="), StringComparison.Ordinal);
            }

            DirectoryNotFoundException missing = await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => jdk.RunAsync(
                    Main,
                    classes,
                    options: options with { WorkingDirectory = Path.Combine(directory, path2: "absent") },
                    cancellationToken: cancellationToken
                )
            );

            Assert.Contains(expectedSubstring: "absent", missing.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(classes, recursive: true);
        }
    }

    // Polls whether a process has ended, as killing a process tree may take a moment to show.
    private static async Task<bool> EndsAsync(int id, TimeSpan within)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed <= within)
        {
            try
            {
                using Process process = Process.GetProcessById(id);

                if (process.HasExited)
                    return true;
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(milliseconds: 100), TestContext.Current.CancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        return false;
    }

    private static string Line(string output, string prefix)
    {
        string line = output.Split(separator: '\n').Select(static line => line.TrimEnd(trimChar: '\r')).First(line => line.StartsWith(prefix, StringComparison.Ordinal));

        return line[prefix.Length..];
    }

    private static int Number(string line)
    {
        return int.Parse(line.Split(separator: ' ')[1], CultureInfo.InvariantCulture);
    }
}

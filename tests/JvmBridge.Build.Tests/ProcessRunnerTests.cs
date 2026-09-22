using System.Diagnostics;

using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Build.Tests;

/// <summary>
/// Verifies that external tooling processes retain evidence and cannot escape timeouts.
/// </summary>
public sealed class ProcessRunnerTests
{
    /// <summary>Inherited output handles cannot make process capture outlive its timeout.</summary>
    [Fact]
    public async Task ExitedParentDoesNotLeaveOutputCaptureUnboundedAsync()
    {
        if (OperatingSystem.IsWindows())
            return;

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-orphan-output-");

        string log = Path.Combine(temporary.Path, path2: "output.log");
        ProcessRunner runner = new(RepositoryContext.Discover());
        Stopwatch elapsed = Stopwatch.StartNew();

        TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(
            () => runner.RunAsync(
                ["/bin/sh", "-c", "sleep 30 & child=$!; printf '%s\\n' \"$child\""],
                log,
                timeout: TimeSpan.FromMilliseconds(milliseconds: 500),
                cancellationToken: CancellationToken.None
            )
        );

        Assert.Contains(expectedSubstring: "Timed out", failure.Message, StringComparison.Ordinal);
        string identifier = (await File.ReadAllTextAsync(log, CancellationToken.None)).Trim();

        using Process descendant = Process.GetProcessById(int.Parse(identifier, System.Globalization.CultureInfo.InvariantCulture));

        ProcessRunner.Terminate(descendant);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(seconds: 10));
    }

    /// <summary>
    /// Verifies that a successful command returns and persists its output.
    /// </summary>
    [Fact]
    public async Task SuccessfulProcessRetainsOutputAsync()
    {
        using TemporaryDirectory temporary = new(prefix: "jvmbridge-process-test-");

        string log = Path.Combine(temporary.Path, path2: "success.log");
        ProcessRunner runner = new(RepositoryContext.Discover());
        ProcessResult result = await runner.RunAsync(SuccessCommand(), log, cancellationToken: CancellationToken.None);

        Assert.Equal(expected: 0, result.ExitCode);
        Assert.Contains(expectedSubstring: "fixture-ok", result.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "fixture-ok", await File.ReadAllTextAsync(log, CancellationToken.None), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a timeout terminates descendants and preserves output already produced.
    /// </summary>
    [Fact]
    public async Task TimeoutTerminatesDescendantsAndRetainsOutputAsync()
    {
        using TemporaryDirectory temporary = new(prefix: "jvmbridge-timeout-test-");

        string log = Path.Combine(temporary.Path, path2: "timeout.log");
        ProcessRunner runner = new(RepositoryContext.Discover());
        Stopwatch elapsed = Stopwatch.StartNew();

        TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(
            () => runner.RunAsync(DescendantCommand(), log, timeout: TimeSpan.FromMilliseconds(milliseconds: 500), cancellationToken: CancellationToken.None)
        );

        elapsed.Stop();
        Assert.Contains(expectedSubstring: "Timed out", failure.Message, StringComparison.Ordinal);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(seconds: 20), $"Process termination took {elapsed.Elapsed}.");

        string evidence = await File.ReadAllTextAsync(log, CancellationToken.None);
        string identifierText = evidence.Split(separator: '\n', StringSplitOptions.RemoveEmptyEntries).First();
        int identifier = int.Parse(identifierText, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(await HasExitedAsync(identifier), $"Descendant process {identifier} survived its parent timeout.");
    }

    private static IReadOnlyList<string> DescendantCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            const string script = "$child = Start-Process -PassThru -FilePath $env:ComSpec -ArgumentList '/d','/c','ping -n 60 127.0.0.1 > nul'; Write-Output $child.Id; Wait-Process -Id $child.Id";

            return ["powershell", "-NoProfile", "-NonInteractive", "-Command", script];
        }

        return ["/bin/sh", "-c", "sleep 60 & child=$!; printf '%s\\n' \"$child\"; wait \"$child\""];
    }

    private static async Task<bool> HasExitedAsync(int identifier)
    {
        try
        {
            using Process descendant = Process.GetProcessById(identifier);

            await descendant.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(seconds: 5), CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

            return descendant.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> SuccessCommand()
    {
        return OperatingSystem.IsWindows()
            ? ["cmd", "/d", "/s", "/c", "echo fixture-ok"]
            : ["/bin/sh", "-c", "printf 'fixture-ok\\n'"];
    }
}

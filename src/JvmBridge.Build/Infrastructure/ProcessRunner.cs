using System.Collections.Concurrent;
using System.Diagnostics;

using JvmBridge.Build.Models;

namespace JvmBridge.Build.Infrastructure;

internal sealed class ProcessRunner(RepositoryContext repository)
{
    private const int FirstIndex = 0;
    private readonly RepositoryContext _repository = repository;

    public static string? FindExecutable(string name)
    {
        string? path = Environment.GetEnvironmentVariable(variable: "PATH");

        if (string.IsNullOrEmpty(path))
            return null;

        IEnumerable<string> extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable(variable: "PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(separator: ';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(directory.Trim(trimChar: '"'), name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension);

                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    public async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> command,
        string? logPath = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        int? expectedExitCode = 0,
        string? workingDirectory = null,
        bool echo = false,
        CancellationToken cancellationToken = default
    )
    {
        if (command.Count == 0)
            throw new ArgumentException(message: "A process command is required.", nameof(command));

        string executable = command[FirstIndex];

        ProcessStartInfo processStartInformation = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? _repository.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        for (int index = 1; index < command.Count; index++)
            processStartInformation.ArgumentList.Add(command[index]);

        if (environment is not null)
        {
            foreach ((string name, string? value) in environment)
            {
                if (value is null)
                {
                    if (processStartInformation.Environment.ContainsKey(name) && !processStartInformation.Environment.Remove(name))
                        throw new InvalidOperationException("Could not remove process environment variable " + name + ".");
                }
                else
                {
                    processStartInformation.Environment[name] = value;
                }
            }
        }

        ConcurrentQueue<string> output = new();

        using Process process = new() { StartInfo = processStartInformation };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Could not start " + executable + ".");
        }
        catch (Exception failure)
        {
            WriteLog(logPath, failure.ToString());

            throw;
        }

        using CancellationTokenSource captureCancellation = new();

        Task standardOutput = CaptureAsync(process.StandardOutput, output, echo, error: false, captureCancellation.Token);
        Task standardError = CaptureAsync(process.StandardError, output, echo, error: true, captureCancellation.Token);
        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(seconds: 180);
        Task wait = process.WaitForExitAsync(CancellationToken.None);

        try
        {
            await Task.WhenAll(wait, standardOutput, standardError).WaitAsync(effectiveTimeout, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (TimeoutException)
        {
            Terminate(process);
            await captureCancellation.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);
            await DrainAsync(wait, standardOutput, standardError).ConfigureAwait(continueOnCapturedContext: false);
            string timedOutput = ReadOutput(output);
            WriteLog(logPath, timedOutput);

            throw new TimeoutException($"Timed out after {effectiveTimeout.TotalSeconds:0.###}s: {logPath ?? executable}");
        }
        catch (OperationCanceledException)
        {
            Terminate(process);
            await captureCancellation.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);
            await DrainAsync(wait, standardOutput, standardError).ConfigureAwait(continueOnCapturedContext: false);
            WriteLog(logPath, ReadOutput(output));

            throw;
        }

        await Task.WhenAll(standardOutput, standardError).ConfigureAwait(continueOnCapturedContext: false);
        string resultOutput = ReadOutput(output);
        WriteLog(logPath, resultOutput);

        if (expectedExitCode is not null && process.ExitCode != expectedExitCode)
        {
            string tail = resultOutput.Length <= 3000 ? resultOutput : resultOutput.Substring(resultOutput.Length - 3000, length: 3000);

            throw new InvalidOperationException($"Exit {process.ExitCode}: {logPath ?? executable}\n{tail}");
        }

        return new ProcessResult(process.ExitCode, resultOutput);
    }

    public async Task RunCheckedAsync(
        IReadOnlyList<string> command,
        string? logPath = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null,
        bool echo = false,
        CancellationToken cancellationToken = default
    )
    {
        ProcessResult result = await RunAsync(command, logPath, timeout, environment, expectedExitCode: 0, workingDirectory, echo, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(message: "A checked process returned a nonzero exit code.");
    }

    internal static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return;
        }
    }

    private static async Task CaptureAsync(StreamReader reader, ConcurrentQueue<string> output, bool echo, bool error, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false) is { } line)
            {
                output.Enqueue(line);

                if (echo)
                {
                    TextWriter writer = error ? Console.Error : Console.Out;
                    await writer.WriteLineAsync(line).ConfigureAwait(continueOnCapturedContext: false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
    }

    private static async Task DrainAsync(Task wait, Task standardOutput, Task standardError)
    {
        try
        {
            await Task.WhenAll(wait, standardOutput, standardError).WaitAsync(TimeSpan.FromSeconds(seconds: 10)).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (TimeoutException)
        {
            ConsoleOutput.WriteLine(message: "Process output readers did not finish within the termination grace period.");
        }
    }

    private static string ReadOutput(ConcurrentQueue<string> output)
    {
        string[] lines = [.. output];

        return lines.Length == 0 ? string.Empty : string.Join(separator: '\n', lines) + "\n";
    }

    private static void WriteLog(string? path, string output)
    {
        if (path is null)
            return;

        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            FileSystem.EnsureDirectory(directory);

        TextFile.Write(path, output);
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;

namespace JvmBridge.Testing;

internal static class ProcessExecution
{
    internal static async Task<JavaRun> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        ProcessStartInfo start = new()
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false
        };

        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        foreach (KeyValuePair<string, string> variable in environment)
            start.Environment[variable.Key] = variable.Value;

        using Process process = new() { StartInfo = start };

        ConcurrentQueue<string> lines = new();

        process.OutputDataReceived += (sender, data) => Enqueue(lines, data.Data);
        process.ErrorDataReceived += (sender, data) => Enqueue(lines, data.Data);

        if (!process.Start())
            throw new InvalidOperationException("Could not start " + executable + ".");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        limit.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);

            throw new TimeoutException(
                $"{Path.GetFileName(executable)} did not finish within {timeout.TotalSeconds:0.#} seconds. Output so far:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}"
            );
        }

        // Waiting again without a token lets the asynchronous readers deliver the last lines.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

        return new JavaRun(process.ExitCode, string.Join(Environment.NewLine, lines));
    }

    private static void Enqueue(ConcurrentQueue<string> lines, string? line)
    {
        if (line is not null)
            lines.Enqueue(line);
    }
}

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace JvmBridge.Testing;

/// <summary>
/// A Java program that <see cref="Jdk.StartAsync"/> started and that runs while the test waits for its output, writes to its input and stops it, as a server is driven. Standard output and standard error are collected line by line in the order
/// they arrive, as in <see cref="JavaRun.Output"/>. When <see cref="JavaRunOptions.Timeout"/> runs out, the program and the processes it started are ended and every wait fails. Dispose it to end a program that still runs.
/// </summary>
public sealed class JavaProcess : IAsyncDisposable
{
    // The program, and the name that messages call it by.
    private readonly string _name;
    private readonly Process _process;

    // The time limit, and the task that ends the program when the limit runs out or the process is disposed.
    private readonly Task _exit;
    private readonly CancellationTokenSource _limit;
    private readonly TimeSpan _timeout;

    // Set once each: the process was disposed, the program ended, the time limit ended it.
    private int _disposed;
    private int _ended;
    private int _timedOut;

    // The lines so far, and a signal that is replaced, and completed, whenever a line arrives or the program ends, so that a wait looks at the lines again.
    private ImmutableList<string> _lines = [];
    private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private JavaProcess(Process process, string name, TimeSpan timeout)
    {
        _process = process;
        _name = name;
        _timeout = timeout;

        _process.OutputDataReceived += (sender, data) => Receive(data.Data);
        _process.ErrorDataReceived += (sender, data) => Receive(data.Data);

        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("Could not start " + name + ".");
        }
        catch
        {
            _process.Dispose();

            throw;
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        _limit = new CancellationTokenSource(timeout);
        _exit = WatchAsync();
    }

    /// <summary>Gets what the program has written so far to standard output and standard error, in the order it arrived.</summary>
    /// <value>The lines joined by the line break of the platform; empty until the program writes one, and longer with each line while it runs.</value>
    public string Output => string.Join(Environment.NewLine, Volatile.Read(ref _lines));

    /// <summary>Ends the program and the processes it started when it still runs, and releases the process. Disposing a program that has ended does nothing more.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) == 1)
            return;

        await _limit.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);
        await _exit.ConfigureAwait(continueOnCapturedContext: false);

        _limit.Dispose();
        _process.Dispose();
    }

    /// <summary>Waits for the program to end by itself, as a server does after <c>stop</c>, and returns its exit code and whole output.</summary>
    /// <param name="cancellationToken">Ends the wait; the program goes on running until it is disposed.</param>
    /// <returns>The exit code and combined output.</returns>
    /// <exception cref="TimeoutException">The program was still running when <see cref="JavaRunOptions.Timeout"/> ran out, and was ended.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the program ended; the message holds the output so far.</exception>
    public async Task<JavaRun> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _exit.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException($"The wait for {_name} to end was cancelled. Output so far:{Environment.NewLine}{Output}",
                innerException: null,
                cancellationToken);
        }

        return Volatile.Read(ref _timedOut) == 1
            ? throw new TimeoutException($"{_name} did not finish within {_timeout.TotalSeconds:0.#} seconds. Output so far:{Environment.NewLine}{Output}")
            : new JavaRun(_process.ExitCode, Output);
    }

    /// <summary>Waits for a line of output that contains a text, which may have arrived before the call.</summary>
    /// <param name="text">The text to look for, such as <c>Done (</c>.</param>
    /// <param name="cancellationToken">Ends the wait; the program goes on running until it is disposed.</param>
    /// <returns>The output up to and including the line.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">The program ended without writing the text.</exception>
    /// <exception cref="TimeoutException"><see cref="JavaRunOptions.Timeout"/> ran out first, and the program was ended.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled first.</exception>
    public async Task<string> WaitForOutputAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        int searched = 0;
        bool ended = false;

        while (!ended)
        {
            // The signal is read first, so a line that arrives after the lines are read completes it, and the end is read before the lines, so they are all there once it is seen.
            Task changed = Volatile.Read(ref _signal).Task;

            ended = Volatile.Read(ref _ended) == 1;

            ImmutableList<string> lines = Volatile.Read(ref _lines);

            for (int index = searched; index < lines.Count; index++)
            {
                if (lines[index].Contains(text, StringComparison.Ordinal))
                    return string.Join(Environment.NewLine, lines.Take(index + 1));
            }

            searched = lines.Count;

            if (ended)
                continue;

            try
            {
                await changed.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException($"The wait for \"{text}\" from {_name} was cancelled. Output so far:{Environment.NewLine}{Output}",
                    innerException: null,
                    cancellationToken);
            }
        }

        string output = string.Join(Environment.NewLine, Volatile.Read(ref _lines));

        if (Volatile.Read(ref _timedOut) == 1)
            throw new TimeoutException($"{_name} did not write \"{text}\" within {_timeout.TotalSeconds:0.#} seconds. Output so far:{Environment.NewLine}{output}");

        throw new InvalidOperationException($"{_name} ended without writing \"{text}\". Output:{Environment.NewLine}{output}");
    }

    /// <summary>Writes a line to the program's standard input, as a person types a command into a server's console.</summary>
    /// <param name="line">The line, without its end.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The program has ended, or its input was closed.</exception>
    public async Task WriteAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);

        try
        {
            await _process.StandardInput
                .WriteLineAsync(line.AsMemory(), cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            throw new InvalidOperationException($"{_name} no longer reads its input: {exception.Message}. Output so far:{Environment.NewLine}{Output}", exception);
        }
    }

    /// <summary>Starts a program with its standard output, standard error and standard input redirected.</summary>
    internal static JavaProcess Start(string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string? workingDirectory,
        TimeSpan timeout)
    {
        if (workingDirectory is not null && !Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"The working directory {workingDirectory} does not exist.");

        ProcessStartInfo start = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            UseShellExecute = false
        };

        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        foreach (KeyValuePair<string, string> variable in environment)
            start.Environment[variable.Key] = variable.Value;

        return new JavaProcess(new Process
        {
            StartInfo = start
        },
            Path.GetFileName(executable),
            timeout);
    }

    /// <summary>Closes the program's standard input, so it reads the end of it, as a program that is given no input does.</summary>
    internal void CloseInput()
    {
        _process.StandardInput.Close();
    }

    // Both readers add lines, each in the order its stream delivers them.
    private void Receive(string? line)
    {
        if (line is null)
            return;

        ImmutableList<string> current;

        do
            current = Volatile.Read(ref _lines);
        while (Interlocked.CompareExchange(ref _lines, current.Add(line), current) != current);

        Interlocked.Exchange(ref _signal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .SetResult();
    }

    // The limit ends the wait when the time runs out, or when the process is disposed, which ends a program that still runs without counting as a timeout. Waiting without a token lets the readers deliver the last lines of both streams.
    private async Task WatchAsync()
    {
        try
        {
            await _process.WaitForExitAsync(_limit.Token).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (OperationCanceledException)
        {
            if (Volatile.Read(ref _disposed) == 0)
                Volatile.Write(ref _timedOut, value: 1);

            _process.Kill(entireProcessTree: true);
        }

        await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

        Volatile.Write(ref _ended, value: 1);

        Interlocked.Exchange(ref _signal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .SetResult();
    }
}

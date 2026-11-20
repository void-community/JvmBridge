namespace JvmBridge.Testing;

internal static class ProcessExecution
{
    /// <summary>Runs a program to its end with no input, collecting both output streams.</summary>
    internal static async Task<JavaRun> RunAsync(string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        JavaProcess process = JavaProcess.Start(executable, arguments, environment, workingDirectory: null, timeout);

        await using (process.ConfigureAwait(continueOnCapturedContext: false))
        {
            process.CloseInput();

            return await process.WaitForExitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }
}

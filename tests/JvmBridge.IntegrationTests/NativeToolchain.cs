using System.Diagnostics;

using JvmBridge.Build.Infrastructure;

namespace JvmBridge.IntegrationTests;

/// <summary>
/// Gives the tests the Visual C++ tools on Windows, where they compile native probes with cl and read exports with dumpbin: the variables that vcvarsall sets for the target's architecture, as a Native Tools command
/// prompt has them, are brought into the test process once, and every process the tests start inherits them. Nothing changes where cl is already on the path, or on another system.
/// </summary>
internal static class NativeToolchain
{
    private static readonly Lazy<bool> Ready = new(Import);

    /// <summary>Brings the tools' environment into the process the first time a test needs them.</summary>
    internal static void Ensure()
    {
        if (!Ready.Value)
            throw new InvalidOperationException(message: "The Visual C++ tools are not installed.");
    }

    private static bool Import()
    {
        if (!OperatingSystem.IsWindows() || ProcessRunner.FindExecutable(name: "cl") is not null)
            return true;

        string programs = Environment.GetEnvironmentVariable(variable: "ProgramFiles(x86)")
            ?? throw new InvalidOperationException(message: "ProgramFiles(x86) is not set.");

        string locator = Path.Combine(programs, path2: "Microsoft Visual Studio", path3: "Installer", path4: "vswhere.exe");
        string installation = Run(locator, arguments: "-latest -products * -property installationPath").Trim();
        string script = Path.Combine(paths: [installation, "VC", "Auxiliary", "Build", "vcvarsall.bat"]);

        // An x86 target is compiled by the x64 compiler, as the runner is an x64 machine.
        string architecture = TestTarget.Rid switch { "win-x86" => "x64_x86", "win-arm64" => "arm64", _ => "x64" };

        foreach (string line in Run(fileName: "cmd.exe", $"/c \"\"{script}\" {architecture} && set\"").Split(separator: '\n'))
        {
            int separator = line.IndexOf(value: '=', StringComparison.Ordinal);

            if (separator > 0)
                Environment.SetEnvironmentVariable(line[..separator], line[(separator + 1)..].TrimEnd(trimChar: '\r'));
        }

        return File.Exists(script);
    }

    private static string Run(string fileName, string arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        })
            ?? throw new InvalidOperationException("Could not start " + fileName + ".");

        string output = process.StandardOutput.ReadToEnd();

        process.WaitForExit();

        return process.ExitCode == 0
            ? output
            : throw new InvalidOperationException($"{fileName} {arguments} exited with {process.ExitCode}:\n{output}");
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;

using JvmBridge.Build;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.IntegrationTests;

internal sealed class JvmScenario(RepositoryContext repository, ProcessRunner processRunner, FixtureCompiler fixtures, NativeAbiVerifier abiVerifier)
{
    private readonly RepositoryContext _repository = repository;
    private readonly ProcessRunner _processRunner = processRunner;
    private readonly FixtureCompiler _fixtures = fixtures;
    private readonly NativeAbiVerifier _abiVerifier = abiVerifier;

    internal async Task<JsonObject> ExerciseAsync(string home, JdkEntry entry, string rid, string compiler, CancellationToken cancellationToken)
    {
        string distribution = entry.Distribution ?? throw new InvalidOperationException(message: "Missing JDK distribution.");
        string identifier = $"{entry.Implementation}-{entry.Java}-{distribution}";
        string directory = Path.Combine(_repository.Artifacts, path2: "results", rid, identifier);
        FileSystem.EnsureDirectory(directory);

        string agent = Path.Combine(
            _repository.Artifacts,
            path2: "agent",
            rid,
            rid.StartsWith(value: "win-", StringComparison.Ordinal) ? "HelloAgent.dll" : rid.StartsWith(value: "osx-", StringComparison.Ordinal) ? "HelloAgent.dylib" : "HelloAgent.so"
        );

        string host = Path.Combine(_repository.Artifacts, path2: "host", rid, rid.StartsWith(value: "win-", StringComparison.Ordinal) ? "JavaHost.exe" : "JavaHost");
        string java = Path.Combine(home, path2: "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        Dictionary<string, string?> environment = [];
        CompatibilityConfiguration configuration = JsonFile.Read<CompatibilityConfiguration>(_repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "compatibility.json"]));
        TargetConfiguration target = configuration.Targets.First(value => value.Rid == rid);

        List<string> vmOptions = [.. configuration.VmOptionRules
            .Where(rule => rule.Java == entry.Java && rule.Implementation == entry.Implementation && rule.Architecture == target.Arch)
            .SelectMany(rule => rule.Options)];

        if (vmOptions.Count > 0)
        {
            environment.Add(
                key: "JAVA_TOOL_OPTIONS",
                string.Join(separator: ' ', new[] { Environment.GetEnvironmentVariable(variable: "JAVA_TOOL_OPTIONS") ?? string.Empty }.Concat(vmOptions)).Trim()
            );
        }

        if (entry.Implementation == "hotspot" && !OperatingSystem.IsWindows())
        {
            string libraryName = OperatingSystem.IsMacOS() ? "libjsig.dylib" : "libjsig.so";
            string? libjsig = Directory.EnumerateFiles(home, libraryName, SearchOption.AllDirectories).FirstOrDefault();

            if (libjsig is not null)
                environment.Add(OperatingSystem.IsMacOS() ? "DYLD_INSERT_LIBRARIES" : "LD_PRELOAD", libjsig);
        }

        string fixture = Path.Combine(directory, path2: "classes");
        string precompiled = Path.Combine(_repository.Artifacts, path2: "fixtures");

        bool hasFixtures = File.Exists(Path.Combine(precompiled, path2: "BridgeFixture.class")) && File.Exists(Path.Combine(precompiled, path2: "AttachFixture.class"));

        if (hasFixtures)
        {
            FileSystem.EnsureDirectory(fixture);

            foreach (string binary in Directory.EnumerateFiles(precompiled, searchPattern: "*.class"))
                File.Copy(binary, Path.Combine(fixture, Path.GetFileName(binary)), overwrite: true);
        }
        else
        {
            await _fixtures.CompileAsync(home, fixture, entry.Java, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        Dictionary<string, long> managed = ParseLongDictionary(
            (await _processRunner.RunAsync([host, "--abi"], Path.Combine(directory, path2: "abi-managed.json"), cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).Output
        );

        Dictionary<string, long> native = await CompileProbeAsync(home, directory, compiler, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        NativeAbiVerifier.Verify(native, managed);
        string jvm = FindLibrary(home, OperatingSystem.IsWindows() ? "jvm.dll" : OperatingSystem.IsMacOS() ? "libjvm.dylib" : "libjvm.so");

        if (OperatingSystem.IsWindows())
        {
            environment.Add(
                key: "PATH",
                Path.Combine(home, path2: "bin") + Path.PathSeparator.ToString() + (Environment.GetEnvironmentVariable(variable: "PATH") ?? string.Empty)
            );
        }
        else if (!OperatingSystem.IsMacOS())
        {
            DirectoryInfo parent = Directory.GetParent(jvm) ?? throw new InvalidOperationException(message: "JVM library has no parent directory.");
            List<string> search = [parent.FullName, parent.Parent?.FullName ?? parent.FullName, Path.Combine(home, path2: "lib"), Environment.GetEnvironmentVariable(variable: "LD_LIBRARY_PATH") ?? string.Empty];
            environment.Add(key: "LD_LIBRARY_PATH", string.Join(Path.PathSeparator, search));
        }

        string nativeProbe = Path.Combine(directory, OperatingSystem.IsWindows() ? "abi.exe" : "abi");

        ProcessResult nativeRun = await _processRunner.RunAsync(
            [nativeProbe, jvm],
            Path.Combine(directory, path2: "native-invocation.log"),
            environment: environment,
            cancellationToken: cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        string baselineLine = nativeRun.Output.Split(separator: '\n').FirstOrDefault(line => line.StartsWith(value: "{\"create\":", StringComparison.Ordinal)) ?? throw new InvalidOperationException(message: "Native invocation did not report its JVM result.");
        Dictionary<string, int> baseline = JsonSerializer.Deserialize<Dictionary<string, int>>(baselineLine, JsonFile.Options) ?? throw new InvalidOperationException(message: "Invalid native invocation result.");
        int destroy = baseline.GetValueOrDefault(key: "destroy");

        ProcessResult hosted = await _processRunner.RunAsync(
            [host, jvm, destroy.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Path.Combine(directory, path2: "host.log"),
            environment: environment,
            cancellationToken: cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        string destroyMarker = "DESTROY_RESULT=" + destroy.ToString(System.Globalization.CultureInfo.InvariantCulture);

        bool hostedJvmMatchesBaseline = hosted.Output.Contains(value: "HOST_OK", StringComparison.Ordinal) && hosted.Output.Contains(destroyMarker, StringComparison.Ordinal);

        if (!hostedJvmMatchesBaseline)
            throw new InvalidOperationException(message: "Embedded JVM behavior differs from the native invocation baseline");

        ProcessResult plain = await _processRunner.RunAsync(
            [java, "-Xcheck:jni", "-cp", fixture, "BridgeFixture", "baseline"],
            Path.Combine(directory, path2: "java-baseline.log"),
            environment: environment,
            cancellationToken: cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (!plain.Output.Contains(value: "JAVA_BASELINE_OK", StringComparison.Ordinal))
            throw new InvalidOperationException(message: "Uninstrumented Java baseline failed");

        ProcessResult result = await _processRunner.RunAsync(
            [java, "-Xcheck:jni", "-agentpath:" + agent, "-cp", fixture, "BridgeFixture"],
            Path.Combine(directory, path2: "startup.log"),
            environment: environment,
            cancellationToken: cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        foreach (string marker in new[] { "AGENT_OK", "SIGNAL_EXCEPTIONS_OK", "VM_INIT", "REGISTER_NATIVES", "TRANSFORM", "VM_DEATH", "UNLOAD" })
        {
            if (!result.Output.Contains(marker, StringComparison.Ordinal))
                throw new InvalidOperationException("Missing " + marker + " in " + identifier);
        }

        Dictionary<string, int> plainWarnings = Warnings(plain.Output);

        Dictionary<string, int> introduced = Warnings(result.Output)
            .Where(value => value.Value > plainWarnings.GetValueOrDefault(value.Key))
            .ToDictionary(value => value.Key, value => value.Value - plainWarnings.GetValueOrDefault(value.Key));

        if (introduced.Count > 0 || result.Output.Contains(value: "NATIVE_ERROR", StringComparison.Ordinal))
            throw new InvalidOperationException("Agent introduced JNI diagnostics: " + JsonSerializer.Serialize(introduced, JsonFile.Options));

        ProcessResult failed = await _processRunner.RunAsync(
            [java, "-agentpath:" + agent + "=fail-start", "-version"],
            Path.Combine(directory, path2: "failed-start.log"),
            environment: environment,
            expectedExitCode: null,
            cancellationToken: cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (failed.ExitCode == 0 || !failed.Output.Contains(value: "Intentional initialization failure", StringComparison.Ordinal))
            throw new InvalidOperationException(message: "Agent initialization failure was not propagated");

        ProcessResult callback = await _processRunner.RunAsync(
            [java, "-agentpath:" + agent + "=fail-callback", "-cp", fixture, "BridgeFixture"],
            Path.Combine(directory, path2: "failed-callback.log"),
            environment: environment,
            cancellationToken: cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        bool callbackFailed = !callback.Output.Contains(value: "Intentional callback failure", StringComparison.Ordinal) || !callback.Output.Contains(value: "AGENT_OK", StringComparison.Ordinal);

        if (callbackFailed)
            throw new InvalidOperationException(message: "Callback exception was not contained");

        await AttachTestAsync(java, agent, fixture, home, directory, entry.Java, environment, entry.Implementation, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        JsonObject outcome = JsonFile.ToObject(entry);
        outcome.SetProperty(propertyName: "status", value: "passed");
        outcome.SetProperty(propertyName: "abi", value: "passed");
        outcome.SetProperty(propertyName: "startup", value: "passed");
        outcome.SetProperty(propertyName: "attach", value: "passed");
        outcome.SetProperty(propertyName: "host", value: "passed");
        outcome.SetProperty(propertyName: "nativeDestroyResult", JsonValue.Create(destroy));
        JsonNode?[] optionNodes = [.. vmOptions.Select(value => (JsonNode?)JsonValue.Create(value))];
        outcome.SetProperty(propertyName: "vmOptions", new JsonArray(optionNodes));
        outcome.SetProperty(propertyName: "baselineJniWarnings", JsonSerializer.SerializeToNode(plainWarnings, JsonFile.Options));
        outcome.SetProperty(
            propertyName: "retransformation",
            result.Output.Contains(value: "RETRANSFORM_OK", StringComparison.Ordinal) ? "passed" : "unavailable"
        );

        return outcome;
    }

    private static async Task CaptureAttachAsync(StreamReader reader, ConcurrentQueue<string> lines, ChannelWriter<string>? writer)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(continueOnCapturedContext: false) is { } line)
            {
                lines.Enqueue(line + "\n");

                if (writer is not null)
                    await writer.WriteAsync(line).ConfigureAwait(continueOnCapturedContext: false);
            }
        }
        finally
        {
            if (writer is not null && !writer.TryComplete())
                ConsoleOutput.WriteLine(message: "The attach output channel was already complete.");
        }
    }

    private static string FindLibrary(string home, string name)
    {
        return Directory.EnumerateFiles(home, name, SearchOption.AllDirectories).FirstOrDefault() ?? throw new InvalidOperationException("Missing " + name + " in " + home);
    }

    private static Dictionary<string, long> ParseLongDictionary(string value)
    {
        return JsonSerializer.Deserialize<Dictionary<string, long>>(value.Trim(), JsonFile.Options) ?? throw new InvalidOperationException(message: "Invalid ABI JSON.");
    }

    private static Dictionary<string, int> Warnings(string output)
    {
        Dictionary<string, int> warnings = [];

        foreach (string line in output.Split(separator: '\n'))
        {
            string value = line.Trim();

            bool diagnostic = value.Contains(value: "JNI WARNING", StringComparison.Ordinal) || value.Contains(value: "WARNING in native method", StringComparison.Ordinal) || Regex.IsMatch(value, pattern: "JVMJNCK[0-9]+[WE]", RegexOptions.CultureInvariant);

            if (!diagnostic)
                continue;

            warnings[value] = warnings.GetValueOrDefault(value) + 1;
        }

        return warnings;
    }

    private async Task AttachTestAsync(
        string java,
        string agent,
        string fixture,
        string home,
        string directory,
        int major,
        IReadOnlyDictionary<string, string?> environment,
        string implementation,
        CancellationToken cancellationToken
    )
    {
        ProcessStartInfo processStartInformation = new()
        {
            FileName = java,
            WorkingDirectory = _repository.Root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        if (major >= 21 && implementation == "hotspot")
            processStartInformation.ArgumentList.Add(item: "-XX:+EnableDynamicAgentLoading");

        foreach (string argument in new[] { "-cp", fixture, "BridgeFixture", "attach" })
            processStartInformation.ArgumentList.Add(argument);

        foreach ((string name, string? value) in environment)
        {
            if (value is null)
            {
                if (processStartInformation.Environment.ContainsKey(name) && !processStartInformation.Environment.Remove(name))
                    throw new InvalidOperationException("Could not remove attach environment variable " + name + ".");
            }
            else
            {
                processStartInformation.Environment[name] = value;
            }
        }

        using Process process = new() { StartInfo = processStartInformation };

        if (!process.Start())
            throw new InvalidOperationException(message: "Could not start the attach target.");

        ConcurrentQueue<string> output = new();
        ConcurrentQueue<string> errors = new();
        Channel<string> events = Channel.CreateUnbounded<string>();
        Task outputReader = CaptureAttachAsync(process.StandardOutput, output, events.Writer);
        Task errorReader = CaptureAttachAsync(process.StandardError, errors, writer: null);

        try
        {
            using CancellationTokenSource readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            readyTimeout.CancelAfter(TimeSpan.FromSeconds(seconds: 45));

            try
            {
                while (await events.Reader.ReadAsync(readyTimeout.Token).ConfigureAwait(continueOnCapturedContext: false) != "READY")
                {
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(message: "Attach target did not become ready within 45 seconds.");
            }

            string classpath = fixture;

            if (major == 8)
                classpath += Path.PathSeparator.ToString() + Path.Combine(home, path2: "lib", path3: "tools.jar");

            await _processRunner.RunCheckedAsync(
                [java, "-cp", classpath, "AttachFixture", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), agent],
                Path.Combine(directory, path2: "attach-controller.log"),
                environment: environment,
                cancellationToken: cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false);
            await process.StandardInput.WriteLineAsync().ConfigureAwait(continueOnCapturedContext: false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            using CancellationTokenSource exitTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            exitTimeout.CancelAfter(TimeSpan.FromSeconds(seconds: 90));

            try
            {
                await process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(message: "Attach target did not exit within 90 seconds.");
            }

            await Task.WhenAll(outputReader, errorReader).ConfigureAwait(continueOnCapturedContext: false);

            bool missingOutput = !string.Concat(output).Contains(value: "AGENT_OK", StringComparison.Ordinal) || !string.Concat(errors).Contains(value: "ATTACH", StringComparison.Ordinal);

            if (process.ExitCode != 0 || missingOutput)
                throw new InvalidOperationException(message: "Late attachment failed");
        }
        finally
        {
            ProcessRunner.Terminate(process);

            try
            {
                await Task.WhenAll(outputReader, errorReader).WaitAsync(TimeSpan.FromSeconds(seconds: 5), CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (TimeoutException)
            {
                ConsoleOutput.WriteLine(message: "Attach output readers did not finish within the termination grace period.");
            }

            TextFile.Write(Path.Combine(directory, path2: "attach-target.log"), string.Concat(output.Concat(errors)));
            process.StandardInput.Close();
        }
    }

    private async Task<Dictionary<string, long>> CompileProbeAsync(string home, string directory, string compiler, CancellationToken cancellationToken)
    {
        string source = Path.Combine(directory, path2: "abi.c");
        TextFile.Write(source, _abiVerifier.CreateProbeSource(Path.Combine(home, path2: "include")));
        string binary = Path.Combine(directory, OperatingSystem.IsWindows() ? "abi.exe" : "abi");
        string headerPlatform = OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
        string[] includes = [Path.Combine(home, path2: "include"), Path.Combine(home, path2: "include", headerPlatform)];
        List<string> command;

        if (OperatingSystem.IsWindows())
        {
            command = [compiler, "/nologo", "/TC", source, "/Fe:" + binary, .. includes.Select(static path => "/I" + path)];
        }
        else
        {
            command = [compiler, source, "-o", binary, .. includes.SelectMany(static path => new[] { "-I", path })];

            if (!OperatingSystem.IsMacOS())
                command.Add(item: "-ldl");
        }

        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            command.AddRange(["-Wl,-pagezero_size,0x100000"]);

        await _processRunner.RunCheckedAsync(command, Path.Combine(directory, path2: "abi-build.log"), cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        string output = (await _processRunner.RunAsync([binary], Path.Combine(directory, path2: "abi-native.json"), cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).Output;

        return ParseLongDictionary(output);
    }
}

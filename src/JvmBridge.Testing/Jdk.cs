using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

using Xunit;

namespace JvmBridge.Testing;

/// <summary>
/// A Java Development Kit for tests. The JDK is the checksum-pinned archive from JvmBridge's inventory, downloaded once and cached under the temporary directory, unless <c>JVMBRIDGE_JDK_&lt;major&gt;</c> names an installed one.
/// Creating a <see cref="Jdk"/> does no work; the first call that needs the JDK resolves it.
/// </summary>
public sealed class Jdk
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Homes = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Initializes a JDK request.</summary>
    /// <param name="major">The Java major version, for example 8 or 17.</param>
    /// <param name="implementation">The JVM implementation, <c>hotspot</c> or <c>openj9</c>.</param>
    public Jdk(int major, string implementation = "hotspot")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(major, other: 8);
        ArgumentException.ThrowIfNullOrEmpty(implementation);
        Major = major;
        Implementation = implementation;
    }

    /// <summary>Gets the JVM implementation.</summary>
    public string Implementation { get; }

    /// <summary>Gets the Java major version.</summary>
    public int Major { get; }

    /// <summary>
    /// Compiles Java sources with this JDK's <c>javac</c> into a new temporary directory.
    /// </summary>
    /// <param name="sources">A source file, a directory, or a pattern such as <c>Fixtures/**/*.java</c>, relative to the test assembly's directory or the current directory.</param>
    /// <param name="release">The Java release to compile for, 8 by default.</param>
    /// <param name="cancellationToken">Cancels the compilation.</param>
    /// <returns>The directory of class files, to use as a class path.</returns>
    public async Task<string> CompileAsync(string sources, int? release = null, CancellationToken cancellationToken = default)
    {
        int target = release ?? 8;
        ArgumentOutOfRangeException.ThrowIfLessThan(target, other: 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(target, Major);
        string home = await GetHomeAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        string[] files = SourceGlob.Resolve(sources, [AppContext.BaseDirectory, Directory.GetCurrentDirectory()]);
        string output = Directory.CreateTempSubdirectory(prefix: "jvmbridge-classes-").FullName;
        string javac = Path.Combine(home, path2: "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac");
        List<string> arguments = Major == 8 ? ["-source", "8", "-target", "8"] : ["--release", target.ToString(CultureInfo.InvariantCulture)];
        arguments.AddRange(["-Xlint:-options", "-encoding", "UTF-8", "-d", output, "-cp", output]);
        arguments.AddRange(files);

        JavaRun run = await ProcessExecution.RunAsync(javac, arguments, new Dictionary<string, string>(), TimeSpan.FromMinutes(minutes: 2), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return run.ExitCode == 0 ? output : throw new InvalidOperationException("javac failed:" + Environment.NewLine + run.Output);
    }

    /// <summary>Gets the JDK's home directory, downloading and unpacking the pinned archive on first use. The test is skipped when this platform has no pinned JDK for the version.</summary>
    /// <param name="cancellationToken">Cancels waiting for the JDK.</param>
    /// <returns>The home directory, which contains <c>bin/java</c>.</returns>
    public Task<string> GetHomeAsync(CancellationToken cancellationToken = default)
    {
        string key = Implementation + "-" + Major.ToString(CultureInfo.InvariantCulture);
        Lazy<Task<string>> pending = Homes.GetOrAdd(key, static (name, jdk) => new Lazy<Task<string>>(() => jdk.ResolveHomeAsync()), this);

        return pending.Value.WaitAsync(cancellationToken);
    }

    /// <summary>Runs a class with this JDK.</summary>
    /// <param name="mainClass">The class with a <c>main</c> method, for example <c>pkg.Main</c>.</param>
    /// <param name="classPath">The class path, for example the directory <see cref="CompileAsync"/> returned.</param>
    /// <param name="agent">An agent to load with <c>-agentpath</c>, or <see langword="null"/> to run without one.</param>
    /// <param name="options">Further options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The exit code and combined output.</returns>
    public async Task<JavaRun> RunAsync(
        string mainClass,
        string classPath,
        PublishedAgent? agent = null,
        JavaRunOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(mainClass);
        ArgumentException.ThrowIfNullOrEmpty(classPath);
        JavaRunOptions settings = options ?? new JavaRunOptions();
        string home = await GetHomeAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        string java = Path.Combine(home, path2: "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        List<string> arguments = [];

        if (settings.CheckJni)
            arguments.Add(item: "-Xcheck:jni");

        arguments.AddRange(settings.JvmArguments);

        if (agent is not null)
            arguments.Add("-agentpath:" + agent.FilePath + (string.IsNullOrEmpty(settings.AgentOptions) ? string.Empty : "=" + settings.AgentOptions));

        arguments.AddRange(["-cp", classPath, mainClass]);
        arguments.AddRange(settings.Arguments);

        Dictionary<string, string> environment = [];

        if (agent is not null && settings.PreloadSignalChaining && Implementation == "hotspot" && !OperatingSystem.IsWindows())
        {
            string library = OperatingSystem.IsMacOS() ? "libjsig.dylib" : "libjsig.so";

            if (Directory.EnumerateFiles(home, library, SearchOption.AllDirectories).FirstOrDefault() is { } signals)
                environment[OperatingSystem.IsMacOS() ? "DYLD_INSERT_LIBRARIES" : "LD_PRELOAD"] = signals;
        }

        return await ProcessExecution.RunAsync(java, arguments, environment, settings.Timeout, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Java {Major.ToString(CultureInfo.InvariantCulture)} ({Implementation})";
    }

    internal string UnavailableMessage(string runtimeIdentifier, string? reason, string installedVariable)
    {
        string detail = reason is null ? string.Empty : ": " + reason;

        return $"No pinned {this} for {runtimeIdentifier}{detail}. Set {installedVariable} to use an installed JDK.";
    }

    private static JdkArchive ToArchive(JdkEntry entry)
    {
        return new JdkArchive
        {
            Distribution = entry.Distribution ?? throw new InvalidOperationException(message: "Missing JDK distribution."),
            Version = entry.Version ?? throw new InvalidOperationException(message: "Missing JDK version."),
            Url = entry.Url ?? throw new InvalidOperationException(message: "Missing JDK URL."),
            Sha256 = entry.Sha256 ?? throw new InvalidOperationException(message: "Missing JDK SHA256.")
        };
    }

    private JdkEntry? FindPinned(string runtimeIdentifier)
    {
        using Stream stream = typeof(Jdk).Assembly.GetManifestResourceStream(name: "jdks.lock.json") ?? throw new InvalidOperationException(message: "The pinned JDK inventory is missing.");

        JdkLock inventory = JsonSerializer.Deserialize<JdkLock>(stream, JsonOptions) ?? throw new InvalidOperationException(message: "The pinned JDK inventory is empty.");
        JdkEntry[] matches = [.. inventory.Jdks.Where(entry => entry.Rid == runtimeIdentifier && entry.Java == Major && entry.Implementation == Implementation)];

        return matches.FirstOrDefault(entry => entry.Status == "available") ?? matches.FirstOrDefault();
    }

    private async Task<string> ResolveHomeAsync()
    {
        string installedVariable = "JVMBRIDGE_JDK_" + Major.ToString(CultureInfo.InvariantCulture);
        string? installed = Environment.GetEnvironmentVariable(installedVariable);

        if (!string.IsNullOrEmpty(installed) && Directory.Exists(installed))
            return installed;

        string runtimeIdentifier = Environment.GetEnvironmentVariable(variable: "TARGET_RID") ?? System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        JdkEntry? entry = FindPinned(runtimeIdentifier);

        if (entry is not { Status: "available", Sha256.Length: >= 12 })
        {
            Assert.Skip(UnavailableMessage(runtimeIdentifier, entry?.Reason, installedVariable));

            throw new InvalidOperationException(message: "Assert.Skip returned.");
        }

        string cache = Path.Combine(
            Path.GetTempPath(),
            path2: "jvmbridge-testing",
            path3: "jdks",
            $"{Implementation}-{Major.ToString(CultureInfo.InvariantCulture)}-{entry.Sha256[..12]}"
        );

        string marker = Path.Combine(cache, path2: "home.txt");

        if (File.Exists(marker))
        {
            string relativeHome = (await File.ReadAllTextAsync(marker).ConfigureAwait(continueOnCapturedContext: false)).Trim();
            string cached = Path.Combine(cache, relativeHome);

            if (Directory.Exists(cached))
                return cached;
        }

        string staging = cache + ".staging-" + Guid.NewGuid().ToString(format: "N");

        using HttpService http = new();

        string home = await new JdkArchiveManager(http).UnpackAsync(ToArchive(entry), staging, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
        string relative = Path.GetRelativePath(staging, home);
        await File.WriteAllTextAsync(Path.Combine(staging, path2: "home.txt"), relative).ConfigureAwait(continueOnCapturedContext: false);

        try
        {
            Directory.Move(staging, cache);
        }
        catch (IOException) when (Directory.Exists(cache))
        {
            // Another process unpacked the same JDK first; use its copy.
            Directory.Delete(staging, recursive: true);
        }

        return Path.Combine(cache, relative);
    }
}

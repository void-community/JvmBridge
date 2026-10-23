using System.Collections.Concurrent;
using System.Globalization;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

using Xunit;

namespace JvmBridge.Testing;

/// <summary>
/// A Java Development Kit for tests. The JDK is the checksum-pinned archive from JvmBridge's inventory, downloaded once and cached under the temporary directory, unless <c>JVMBRIDGE_JDK_&lt;major&gt;_&lt;IMPLEMENTATION&gt;</c> (for example
/// <c>JVMBRIDGE_JDK_8_HOTSPOT</c>) or <c>JVMBRIDGE_JDK_&lt;major&gt;</c> names an installed one. An installed JDK is asked for its version and implementation once, and a test fails when they are not the ones it asked for, or when the directory does not exist.
/// Creating a <see cref="Jdk"/> does no work; the first call that needs the JDK resolves it.
/// </summary>
public sealed class Jdk
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Homes = new(StringComparer.Ordinal);

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
    /// <param name="release">The Java release to compile for, 8 by default. Releases 6 and 7 need a JDK that still compiles for them: Java 8 to 11 for 6, Java 8 to 19 for 7.</param>
    /// <param name="cancellationToken">Cancels the compilation.</param>
    /// <returns>The directory of class files, to use as a class path.</returns>
    public async Task<string> CompileAsync(string sources, int? release = null, CancellationToken cancellationToken = default)
    {
        int target = release ?? 8;
        ArgumentOutOfRangeException.ThrowIfLessThan(target, other: 6);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(target, Major);
        string home = await GetHomeAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        string[] files = SourceGlob.Resolve(sources, [AppContext.BaseDirectory, Directory.GetCurrentDirectory()]);
        string output = Directory.CreateTempSubdirectory(prefix: "jvmbridge-classes-").FullName;
        string javac = Path.Combine(home, path2: "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac");
        // javac 8 and older have no --release; javac 9 to 11 compile for Java 6 and later, 12 to 19 for 7 and later, 20 and later for 8 and later.
        string version = target.ToString(CultureInfo.InvariantCulture);
        List<string> arguments = Major <= 8 ? ["-source", version, "-target", version] : ["--release", version];
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
    /// <param name="agent">An agent to load: a native library with <c>-agentpath</c>, a jar (see <see cref="PublishedAgent.FindJar"/>) with <c>-javaagent</c>, or <see langword="null"/> to run without one. <see cref="JavaRunOptions.AgentOptions"/> follow the <c>=</c> either way.</param>
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
            arguments.Add(AgentArgument(agent, settings.AgentOptions));

        arguments.AddRange(["-cp", classPath, mainClass]);
        arguments.AddRange(settings.Arguments);

        Dictionary<string, string> environment = [];

        // A jar starts its agent after the JVM has started, so there is no runtime of the agent to chain signals with yet when the JVM installs its handlers; the preload is only for -agentpath.
        if (agent is { IsJar: false } && settings.PreloadSignalChaining && Implementation == "hotspot" && !OperatingSystem.IsWindows())
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

    /// <summary>Builds the JVM argument that loads an agent: <c>-javaagent:</c> for a jar and <c>-agentpath:</c> for a native library, with the options after <c>=</c>.</summary>
    internal static string AgentArgument(PublishedAgent agent, string? options)
    {
        string route = agent.IsJar ? "-javaagent:" : "-agentpath:";

        return route + agent.FilePath + (string.IsNullOrEmpty(options) ? string.Empty : "=" + options);
    }

    internal string UnavailableMessage(string runtimeIdentifier, string? reason, string installedVariable)
    {
        string detail = reason is null ? string.Empty : ": " + reason;

        return $"No pinned {this} for {runtimeIdentifier}{detail}. Set {installedVariable} to use an installed JDK.";
    }

    /// <summary>
    /// Checks that an installed JDK is the one a test asked for, by asking it: a JDK of another major version or implementation, or a path that is not a JDK, fails the test with what the variable named, what it found and what the test needs, so
    /// that a test labelled Java 8 never passes on another Java.
    /// </summary>
    /// <param name="variable">The environment variable that named the JDK, for the message.</param>
    /// <param name="home">The JDK directory.</param>
    /// <returns>A task that completes when the JDK matches.</returns>
    /// <exception cref="InvalidOperationException">The JDK does not match, or is not a JDK.</exception>
    internal async Task VerifyInstalledAsync(string variable, string home)
    {
        string advice = $"Point {variable} at a {this} JDK, or unset it to use the pinned one.";

        if (!Directory.Exists(home))
            throw new InvalidOperationException($"{variable} names {home}, which does not exist. {advice}");

        string java = Path.Combine(home, path2: "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");

        if (!File.Exists(java))
            throw new InvalidOperationException($"{variable} names {home}, which has no bin/{Path.GetFileName(java)}, so it is not a JDK. {advice}");

        JavaRun probe = await ProcessExecution.RunAsync(
            java,
            ["-XshowSettings:properties", "-version"],
            new Dictionary<string, string>(),
            TimeSpan.FromMinutes(minutes: 1),
            CancellationToken.None
        )
            .ConfigureAwait(continueOnCapturedContext: false);

        InstalledJdk found = InstalledJdk.Parse(probe.Output) ?? throw new InvalidOperationException(
            $"{variable} names {home}, whose java did not report its version (exit code {probe.ExitCode.ToString(CultureInfo.InvariantCulture)}). {advice}"
        );

        if (found.Major == Major && found.Implementation == Implementation)
            return;

        string specific = SpecificVariable();
        string alternative = found.Major == Major && variable != specific ? $" For a {found} JDK beside it, set {specific} for this one." : string.Empty;

        throw new InvalidOperationException($"{variable} names {home}, which is {found}, but this test needs {this}. {advice}{alternative}");
    }

    private async Task<string> ResolveHomeAsync()
    {
        string installedVariable = "JVMBRIDGE_JDK_" + Major.ToString(CultureInfo.InvariantCulture);

        // The variable for one implementation wins, so a suite can name a HotSpot and an OpenJ9 JDK of the same major; the shared one must still match.
        foreach (string variable in new[] { SpecificVariable(), installedVariable })
        {
            if (Environment.GetEnvironmentVariable(variable) is not { Length: > 0 } installed)
                continue;

            await VerifyInstalledAsync(variable, installed).ConfigureAwait(continueOnCapturedContext: false);

            return installed;
        }

        string runtimeIdentifier = Environment.GetEnvironmentVariable(variable: "TARGET_RID") ?? System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        JdkEntry? entry = PinnedJdk.Find(Major, Implementation, runtimeIdentifier);

        if (entry is not { Status: "available", Sha256.Length: >= 12 })
        {
            Assert.Skip(UnavailableMessage(runtimeIdentifier, entry?.Reason, installedVariable));

            throw new InvalidOperationException(message: "Assert.Skip returned.");
        }

        return await PinnedJdk.UnpackAsync(entry, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
    }

    private string SpecificVariable()
    {
        return "JVMBRIDGE_JDK_" + Major.ToString(CultureInfo.InvariantCulture) + "_" + Implementation.ToUpperInvariant();
    }
}

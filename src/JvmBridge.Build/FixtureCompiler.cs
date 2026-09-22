using System.Runtime.InteropServices;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Build;

internal sealed class FixtureCompiler(RepositoryContext repository, JdkArchiveManager archives, ProcessRunner processRunner)
{
    private readonly RepositoryContext _repository = repository;
    private readonly JdkArchiveManager _archives = archives;
    private readonly ProcessRunner _processRunner = processRunner;

    public async Task CompileAllAsync(CancellationToken cancellationToken)
    {
        string runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        JdkEntry compiler = JsonFile.Read<JdkLock>(_repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "jdks.lock.json"])).Jdks.FirstOrDefault(entry => entry.Rid == runtimeIdentifier && entry.Java == 17 && entry.Implementation == "hotspot" && entry.Status == "available") ?? throw new InvalidOperationException("No pinned Java 17 compiler is available for " + runtimeIdentifier + ".");

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-compiler-");

        string home = await _archives.UnpackAsync(ToArchive(compiler), temporary.Path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        string output = Path.Combine(_repository.Artifacts, path2: "fixtures");
        await CompileAsync(home, output, major: 17, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        JsonFile.Write(Path.Combine(output, path2: "compiler.json"), compiler);
    }

    public async Task CompileAsync(string home, string output, int major, CancellationToken cancellationToken)
    {
        string compiler = Path.Combine(home, path2: "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac");
        FileSystem.EnsureDirectory(output);
        List<string> arguments = major == 8 ? ["-source", "8", "-target", "8", "-Xlint:-options"] : ["--release", "8"];
        await _processRunner.RunCheckedAsync(
            [compiler, .. arguments, "-cp", output, "-d", output, _repository.PathFromRoot(parts: ["tests", "Fixtures", "BridgeFixture.java"])],
            Path.Combine(output, path2: "javac.log"),
            workingDirectory: output,
            cancellationToken: cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        string classpath = major == 8 ? Path.Combine(home, path2: "lib", path3: "tools.jar") + Path.PathSeparator.ToString() + output : output;
        await _processRunner.RunCheckedAsync(
            [compiler, "-source", "8", "-target", "8", "-Xlint:-options", "-cp", classpath, "-d", output, _repository.PathFromRoot(parts: ["tests", "Fixtures", "AttachFixture.java"])],
            Path.Combine(output, path2: "javac-attach.log"),
            workingDirectory: output,
            cancellationToken: cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);
    }

    internal static JdkArchive ToArchive(JdkEntry entry)
    {
        return new JdkArchive
        {
            Distribution = entry.Distribution ?? throw new InvalidOperationException(message: "Missing JDK distribution."),
            Version = entry.Version ?? throw new InvalidOperationException(message: "Missing JDK version."),
            Url = entry.Url ?? throw new InvalidOperationException(message: "Missing JDK URL."),
            Sha256 = entry.Sha256 ?? throw new InvalidOperationException(message: "Missing JDK SHA256.")
        };
    }
}

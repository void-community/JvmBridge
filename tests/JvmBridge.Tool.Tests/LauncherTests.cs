using System.Runtime.InteropServices;

using JvmBridge.Testing;
using JvmBridge.Launcher;

using Xunit;

namespace JvmBridge.Tool.Tests;

/// <summary>Verifies the launcher class that <c>java -javaagent</c> starts.</summary>
public sealed class LauncherTests
{
    /// <summary>The platform the launcher detects is the runtime identifier of the machine, which is how the jar names its libraries.</summary>
    [Theory]
    [JdkData(8)]
    public async Task DetectsThePlatformAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classes = await jdk.CompileAsync(sources: "Launcher/*.java", release: 6, TestContext.Current.CancellationToken);

        try
        {
            JavaRun run = await jdk.RunAsync(mainClass: "jvmbridge.PlatformProbe", classPath: classes, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(expected: 0, run.ExitCode);
            Assert.Contains("platform=" + ExpectedPlatform(), run.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    /// <summary>The launcher class that the tool carries is what Launcher.java compiles to with Java 8 for Java 6, so the source and the checked-in class cannot drift apart.</summary>
    [Theory]
    [JdkData(8)]
    public async Task EmbeddedClassMatchesTheSourceAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classes = await jdk.CompileAsync(sources: "Launcher/*.java", release: 6, TestContext.Current.CancellationToken);

        try
        {
            byte[] compiled = await File.ReadAllBytesAsync(Path.Combine(classes, path2: "jvmbridge", path3: "Launcher.class"), TestContext.Current.CancellationToken);

            Assert.True(
                compiled.AsSpan().SequenceEqual(LauncherJar.ReadLauncher()),
                userMessage: "src/JvmBridge.Launcher/Launcher/Launcher.class is stale. Compile Launcher.java with Java 8 (javac -source 6 -target 6) and copy jvmbridge/Launcher.class over it."
            );
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    private static string ExpectedPlatform()
    {
        string processor = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => throw new NotImplementedException(),
            Architecture.Wasm => throw new NotImplementedException(),
            Architecture.S390x => throw new NotImplementedException(),
            Architecture.LoongArch64 => throw new NotImplementedException(),
            Architecture.Armv6 => throw new NotImplementedException(),
            Architecture.Ppc64le => throw new NotImplementedException(),
            Architecture.RiscV64 => throw new NotImplementedException(),
            _ => "arm"
        };

        if (OperatingSystem.IsWindows())
            return "win-" + processor;

        if (OperatingSystem.IsMacOS())
            return "osx-" + processor;

        bool musl = Directory.Exists(path: "/lib") && Directory.EnumerateFiles(path: "/lib", searchPattern: "ld-musl-*").Any();

        return (musl ? "linux-musl-" : "linux-") + processor;
    }
}

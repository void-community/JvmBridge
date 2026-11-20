using System.IO.Compression;
using System.Runtime.InteropServices;

using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Build.Tests;

/// <summary>Builds a consumer against the freshly packed package to verify the typed proxies that a JavaReference generates, and runs them against a real JVM.</summary>
public sealed class JavaReferenceTests
{
    private const string CalculatorSource = """
        package app;

        public class Calculator {
            private final String name;

            public Calculator(String name) { this.name = name; }

            public static int add(int left, int right) { return left + right; }

            public static String greet(String who) { return who == null ? "nobody" : "hi " + who; }

            public String describe(long value, boolean negate) { return name + ":" + (negate ? -value : value); }

            public Calculator self() { return this; }

            public void noop(Object value) { }

            public void noop(Runnable value) { }

            private void hidden() { }
        }
        """;

    private const string ProgramSource = """
        using JavaProxies.App;
        using JvmBridge.Runtime;

        JavaVirtualMachine machine = JavaVirtualMachine.Create(args[0], "-Djava.class.path=" + args[1]);
        using JavaThreadAttachment attachment = machine.AttachCurrentThread();
        JavaEnvironment environment = attachment.Environment;

        using JavaLocalReference created = Calculator.New(environment, "calc");
        Calculator calculator = new(created.Handle);
        using JavaLocalReference same = calculator.Self(environment);
        calculator.Noop_Object(environment, 0);

        Console.WriteLine(string.Join("|", Calculator.Add(environment, 2, 3), calculator.Describe(environment, 40L, true), Calculator.Greet(environment, null), Calculator.Greet(environment, "agent"), environment.IsSameObject(same.Handle, created.Handle)));
        """;

    /// <summary>A jar that does not exist is reported with its path.</summary>
    [Fact]
    public async Task AMissingJarIsReportedAsync()
    {
        using TemporaryDirectory project = new(prefix: "jvmbridge-javareference-");

        WriteProject(project.Path, types: "app/Calculator", program: "System.Console.WriteLine();");

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path, jdk: null);

        Assert.NotEqual(expected: 0, build.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB013", build.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "app.jar", build.Output, StringComparison.Ordinal);
    }

    /// <summary>A JavaReference without Types says what to add instead of wrapping a whole jar.</summary>
    [Fact]
    public async Task AReferenceWithoutTypesIsReportedAsync()
    {
        using TemporaryDirectory project = new(prefix: "jvmbridge-javareference-");

        WriteProject(project.Path, string.Empty, program: "System.Console.WriteLine();");

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path, jdk: null);

        Assert.NotEqual(expected: 0, build.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB011", build.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "Types", build.Output, StringComparison.Ordinal);
    }

    /// <summary>A Types entry that matches nothing is reported with the entry, which is usually a typo.</summary>
    [Fact]
    public async Task ATypeThatMatchesNothingIsReportedAsync()
    {
        using TemporaryDirectory project = new(prefix: "jvmbridge-javareference-");

        await File.WriteAllBytesAsync(Path.Combine(project.Path, path2: "app.jar"),
            EmptyJar(),
            TestContext.Current.CancellationToken);

        WriteProject(project.Path, types: "app/Typo", program: "System.Console.WriteLine();");

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path, jdk: null);

        Assert.NotEqual(expected: 0, build.ExitCode);
        Assert.Contains(expectedSubstring: "JVMB012", build.Output, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "app/Typo", build.Output, StringComparison.Ordinal);
    }

    /// <summary>A JavaReference with the Types of its classes becomes typed calls that run against a real JVM: static and instance methods, a constructor, strings including <see langword="null"/>, a returned object and overloads that differ by Java type.</summary>
    /// <exception cref="InvalidOperationException">No Java 17 HotSpot JDK is pinned and available for this machine's runtime identifier, or the JVM library found in the unpacked JDK has no directory.</exception>
    [Fact]
    public async Task ATypedProxyCallsRealJavaAsync()
    {
        string runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        RepositoryContext repository = RepositoryContext.Discover();

        JdkEntry entry = PinnedJdks.Jdks
            .FirstOrDefault(candidate => candidate.Rid == runtimeIdentifier && candidate.Java == 17 && candidate.Implementation == "hotspot" && candidate.Status == "available")
            ?? throw new InvalidOperationException("No pinned Java 17 compiler is available for " + runtimeIdentifier + ".");

        using TemporaryDirectory jdk = new(prefix: "jvmbridge-javareference-jdk-");

        using TemporaryDirectory project = new(prefix: "jvmbridge-javareference-");

        using HttpService http = new();

        string home = await new JdkArchiveManager(http).UnpackAsync(FixtureCompiler.ToArchive(entry),
            jdk.Path,
            CancellationToken.None);

        string jar = await BuildJarAsync(repository, home, project.Path);

        WriteProject(project.Path, types: "app/Calculator", ProgramSource);

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path, jdk: null);

        string libjvm = Directory.EnumerateFiles(home, searchPattern: "*jvm.*", SearchOption.AllDirectories)
            .First(path => Path.GetFileName(path) is "libjvm.so" or "libjvm.dylib" or "jvm.dll");

        // The JVM's own libraries name libjvm.so. glibc knows the one that is loaded already; musl looks for it again, so its directory is on the search path, as a Java launcher puts it there.
        string search = Path.GetDirectoryName(libjvm)
            ?? throw new InvalidOperationException(message: "The JVM library has no directory.");

        if (Environment.GetEnvironmentVariable(variable: "LD_LIBRARY_PATH") is { Length: > 0 } inherited)
            search += Path.PathSeparator.ToString() + inherited;

        ProcessResult run = await new ProcessRunner(repository).RunAsync(command: ["dotnet", Path.Combine(paths: [project.Path, "bin", "Release", "net10.0", "Consumer.dll"]), libjvm, jar],
            logPath: null,
            environment: new Dictionary<string, string?>
            {
                [key: "LD_LIBRARY_PATH"] = search
            },
            workingDirectory: project.Path,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(build.ExitCode == 0, build.Output);

        string printed = run.Output
            .Trim()
            .Split(separator: '\n')
            .Last()
            .Trim();

        Assert.Equal(expected: "5|calc:-40|nobody|hi agent|True", printed);
    }

    private static async Task<string> BuildJarAsync(RepositoryContext repository, string home, string directory)
    {
        string sources = Path.Combine(directory, path2: "java", path3: "app");
        string classes = Path.Combine(directory, path2: "classes");

        FileSystem.EnsureDirectory(sources);
        FileSystem.EnsureDirectory(classes);

        await File.WriteAllTextAsync(Path.Combine(sources, path2: "Calculator.java"), CalculatorSource)
            .ConfigureAwait(continueOnCapturedContext: false);

        string javac = Path.Combine(home, path2: "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac");

        await new ProcessRunner(repository)
            .RunCheckedAsync(command: [javac, "--release", "8", "-d", classes, Path.Combine(sources, path2: "Calculator.java")],
                logPath: null,
                environment: new Dictionary<string, string?>
                {
                    [key: "LC_ALL"] = "C.UTF-8"
                })
            .ConfigureAwait(continueOnCapturedContext: false);

        string jar = Path.Combine(directory, path2: "app.jar");

        await ZipFile.CreateFromDirectoryAsync(classes, jar).ConfigureAwait(continueOnCapturedContext: false);

        return jar;
    }

    private static byte[] EmptyJar()
    {
        using MemoryStream stream = new();

        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry manifest = archive.CreateEntry(entryName: "META-INF/MANIFEST.MF");

            using Stream content = manifest.Open();

            content.WriteByte(value: 10);
        }

        return stream.ToArray();
    }

    private static void WriteProject(string directory, string types, string program)
    {
        string metadata = types.Length == 0 ? string.Empty : $" Types=\"{types}\"";

        File.WriteAllText(Path.Combine(directory, path2: "Program.cs"), program);

        File.WriteAllText(Path.Combine(directory, path2: "Consumer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <RollForward>LatestMajor</RollForward>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="JvmBridge" Version="{JavaSourceTests.PackedVersion()}" />
                <JavaReference Include="app.jar"{metadata} />
              </ItemGroup>
            </Project>
            """);
    }
}

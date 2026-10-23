using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

using Xunit;

namespace JvmBridge.Build.Tests;

/// <summary>Builds a consumer whose Java helper names the application's types, through a stub source and a jar on javac's class path, to verify that only the helper is embedded.</summary>
public sealed class JavaCompileReferenceTests
{
    private const string HelperSource = """
        package pkg;

        import app.Account;
        import app.Ledger;

        public final class Typed {
            private Typed() {
            }

            public static int deposit(Account account, int amount) {
                return account.balance + amount * 2 - Ledger.fee();
            }
        }
        """;

    private const string LedgerSource = """
        package app;

        public final class Ledger {
            private Ledger() {
            }

            public static int fee() {
                return 3;
            }
        }
        """;

    private const string ProgramSource = """
        using JvmBridge.Agents;

        Console.WriteLine(string.Join("|", JavaClasses.Pkg.Typed.Name, typeof(JavaClasses).GetNestedType("App") is null ? "no app" : "app"));
        """;

    // The application's class as it is at run time: the field is private, and SetFieldAccess opens it; the stub declares it public so that the helper compiles.
    private const string StubSource = """
        package app;

        public class Account {
            public int balance;
        }
        """;

    /// <summary>
    /// A helper compiles against an application class it only has as a stub source and another it has compiled in a directory, and the build embeds the helper alone: the stub and the referenced class are on javac's class path, not among the
    /// helpers.
    /// </summary>
    [Fact]
    public async Task HelpersCompileAgainstStubsAndReferencesWithoutEmbeddingThemAsync()
    {
        RepositoryContext repository = RepositoryContext.Discover();
        string runtimeIdentifier = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;

        JdkEntry entry = JsonFile.Read<JdkLock>(repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "jdks.lock.json"])).Jdks
            .FirstOrDefault(
                candidate => candidate.Rid == runtimeIdentifier && candidate.Java == 17 && candidate.Implementation == "hotspot" && candidate.Status == "available"
            )
            ?? throw new InvalidOperationException("No pinned Java 17 compiler is available for " + runtimeIdentifier + ".");

        using TemporaryDirectory jdk = new(prefix: "jvmbridge-compile-reference-jdk-");

        using TemporaryDirectory project = new(prefix: "jvmbridge-compile-reference-");

        using HttpService http = new();

        string home = await new JdkArchiveManager(http).UnpackAsync(FixtureCompiler.ToArchive(entry), jdk.Path, TestContext.Current.CancellationToken);
        string references = Path.Combine(project.Path, path2: "refs");

        Write(project.Path, path: "ledger/app/Ledger.java", LedgerSource);
        FileSystem.EnsureDirectory(references);

        ProcessResult compiled = await new ProcessRunner(repository).RunAsync(
            [Path.Combine(home, path2: "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac"), "--release", "8", "-d", references, Path.Combine(project.Path, path2: "ledger", path3: "app", path4: "Ledger.java")],
            logPath: null,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.True(compiled.ExitCode == 0, compiled.Output);

        Write(project.Path, path: "java/pkg/Typed.java", HelperSource);
        Write(project.Path, path: "stubs/app/Account.java", StubSource);
        Write(project.Path, path: "Program.cs", ProgramSource);
        Write(
            project.Path,
            path: "Consumer.csproj",
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                <RollForward>LatestMajor</RollForward>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="JvmBridge" Version="{JavaSourceTests.PackedVersion()}" />
                <JavaSource Include="java/**/*.java" />
                <JavaStubSource Include="stubs/**/*.java" />
                <JavaCompileReference Include="refs" />
              </ItemGroup>
            </Project>
            """
        );

        ProcessResult build = await JavaSourceTests.BuildAsync(project.Path, home);

        Assert.True(build.ExitCode == 0, build.Output);

        string[] embedded = [.. Directory.EnumerateFiles(Path.Combine(project.Path, path2: "obj"), searchPattern: "*.class", SearchOption.AllDirectories)
            .Where(static path => path.Replace(oldChar: '\\', newChar: '/').Contains(value: "/jvmbridge-java/classes/", StringComparison.Ordinal))
            .Select(static path => Path.GetFileName(path))];

        ProcessResult run = await new ProcessRunner(repository).RunAsync(
            ["dotnet", Path.Combine(paths: [project.Path, "bin", "Release", "net10.0", "Consumer.dll"])],
            logPath: null,
            workingDirectory: project.Path,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(["Typed.class"], embedded);
        Assert.Equal(expected: "pkg/Typed|no app", run.Output.Trim());
    }

    private static void Write(string root, string path, string text)
    {
        string full = Path.Combine(root, path);
        FileSystem.EnsureDirectory(Path.GetDirectoryName(full) ?? root);
        File.WriteAllText(full, text);
    }
}

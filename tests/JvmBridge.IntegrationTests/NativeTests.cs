using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

using JvmBridge.Build;

using Xunit;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.IntegrationTests;

/// <summary>Exercises packed NativeAOT consumers against individual JVM inventory cells.</summary>
public sealed class NativeTests
{
    /// <summary>Gets independently reported JVM cells for the selected target.</summary>
    public static IEnumerable<TheoryDataRow<string, int, string>> Cells
    {
        get
        {
            string rid = Environment.GetEnvironmentVariable(variable: "TARGET_RID") ?? RuntimeInformation.RuntimeIdentifier;

            if (Environment.GetEnvironmentVariable(variable: "JVM_JDK_HOME") is not null)
            {
                int major = int.Parse(Environment.GetEnvironmentVariable(variable: "JVM_JAVA_MAJOR") ?? "25", System.Globalization.CultureInfo.InvariantCulture);

                yield return new TheoryDataRow<string, int, string>(rid, major, Environment.GetEnvironmentVariable(variable: "JVM_IMPLEMENTATION") ?? "hotspot");

                yield break;
            }

            RepositoryContext repository = RepositoryContext.Discover();
            JdkLock inventory = JsonFile.Read<JdkLock>(repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "jdks.lock.json"]));

            foreach (JdkEntry entry in inventory.Jdks.Where(entry => entry.Rid == rid))
                yield return new TheoryDataRow<string, int, string>(entry.Rid, entry.Java, entry.Implementation) { Skip = entry.Status == "available" ? null : entry.Reason };
        }
    }

    /// <summary>Gets whether packed native consumers are selected for execution.</summary>
    public static bool NativeEnabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable: "TARGET_RID"));

    /// <summary>Verifies required exports without loading the native agent library.</summary>
    [Fact(
        SkipUnless = nameof(NativeEnabled),
        Skip = "Set TARGET_RID and publish package consumers with the PublishConsumers MSBuild target."
    )]
    public async Task ExportsAsync()
    {
        RepositoryContext repository = RepositoryContext.Discover();
        string rid = Environment.GetEnvironmentVariable(variable: "TARGET_RID") ?? throw new InvalidOperationException(message: "TARGET_RID is required.");
        string extension = rid.StartsWith(value: "win-", StringComparison.Ordinal) ? ".dll" : rid.StartsWith(value: "osx-", StringComparison.Ordinal) ? ".dylib" : ".so";
        string agent = Path.Combine(repository.Artifacts, path2: "agent", rid, "HelloAgent" + extension);
        string symbolTool = ProcessRunner.FindExecutable(name: "llvm-nm") ?? ProcessRunner.FindExecutable(name: "nm") ?? "nm";
        string[] command = OperatingSystem.IsWindows() ? ["dumpbin", "/exports", agent] : [symbolTool, OperatingSystem.IsMacOS() ? "-g" : "-D", agent];

        ProcessResult result = await new ProcessRunner(repository).RunAsync(
            command,
            Path.Combine(repository.Artifacts, path2: "results", rid, path4: "exports.log"),
            cancellationToken: TestContext.Current.CancellationToken
        );

        foreach (string symbol in new[] { "Agent_OnLoad", "Agent_OnAttach", "Agent_OnUnload" })
            Assert.Contains(symbol, result.Output, StringComparison.Ordinal);

        JsonFile.Write(Path.Combine(repository.Artifacts, path2: "results", rid, path4: "build-summary.json"), new BuildSummary { Rid = rid });
    }

    /// <summary>Checks ABI, startup, attach, hosting, Unicode, references, transformations, and failure containment.</summary>
    /// <param name="rid">Native consumer target.</param>
    /// <param name="major">Java major version.</param>
    /// <param name="implementation">JVM implementation.</param>
    [Theory(
        SkipUnless = nameof(NativeEnabled),
        Skip = "Set TARGET_RID and publish package consumers with the PublishConsumers MSBuild target."
    )]
    [MemberData(nameof(Cells))]
    public async Task JvmContractAsync(string rid, int major, string implementation)
    {
        ArgumentNullException.ThrowIfNull(rid);
        RepositoryContext repository = RepositoryContext.Discover();
        string? localHome = Environment.GetEnvironmentVariable(variable: "JVM_JDK_HOME");

        JdkEntry entry = localHome is null
            ? JsonFile.Read<JdkLock>(repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "jdks.lock.json"])).Jdks.Single(entry => entry.Rid == rid && entry.Java == major && entry.Implementation == implementation)
            : new JdkEntry { Rid = rid, Java = major, Implementation = implementation, Distribution = "local", Status = "available" };

        string resultPath = Path.Combine(paths: [repository.Artifacts, "results", rid, $"{implementation}-{major}-{entry.Distribution}", "result.json"]);

        using HttpService http = new();

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-integration-");

        ProcessRunner processes = new(repository);
        JdkArchiveManager archives = new(http);
        JvmScenario scenario = new(repository, processes, new FixtureCompiler(repository, archives, processes), new AbiTool(repository));

        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            string home = localHome ?? await archives.UnpackAsync(FixtureCompiler.ToArchive(entry), temporary.Path, cancellationToken);
            string compiler = Environment.GetEnvironmentVariable(variable: "NATIVE_COMPILER") ?? (OperatingSystem.IsWindows() ? "cl" : "cc");
            JsonObject result = await scenario.ExerciseAsync(Path.GetFullPath(home), entry, rid, compiler, cancellationToken);
            JsonFile.WriteNode(resultPath, result);
        }
        catch (Exception failure)
        {
            JsonObject result = JsonFile.ToObject(entry);
            result.SetProperty(propertyName: "status", value: "failed");
            result.SetProperty(propertyName: "error", failure.ToString());
            JsonFile.WriteNode(resultPath, result);

            throw;
        }
    }
}

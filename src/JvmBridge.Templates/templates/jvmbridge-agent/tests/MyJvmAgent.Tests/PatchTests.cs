using JvmBridge.Testing;

using MyJvmAgent;

using Xunit;

namespace MyJvmAgent.Tests;

/// <summary>Checks the patch on class bytes, then in a JVM with the published agent: its native library through -agentpath and its jar through -javaagent.</summary>
public sealed class PatchTests
{
    /// <summary>The patch redirects the call and says what it changed, without a JVM running the result.</summary>
    [Theory]
    [JdkData(8)]
    public async Task PatchRedirectsTheSortAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classPath = await jdk.CompileAsync("Fixtures/example/*.java", cancellationToken: TestContext.Current.CancellationToken);
        byte[] original = File.ReadAllBytes(Path.Combine(classPath, "example", "Target.class"));

        JvmBridge.Agents.ClassPatchResult result = Patches.SortDescending.Apply(original);
        byte[] patched = result.Bytes ?? throw new InvalidOperationException(result.ToString());

        Assert.True(result.Changed, result.ToString());
        Assert.Equal(expected: 1, result.RedirectedInvocations);
        ClassFileAssert.Calls(patched, "sorted", "redirects/Sorting", "sort");
        ClassFileAssert.DoesNotCall(patched, "sorted", "java/util/Collections", "sort");
    }

    /// <summary>The same class sorts ascending on its own and descending with the agent loaded. The test is skipped until the agent is published for this platform.</summary>
    [Theory]
    [JdkData(8, 17, 21)]
    public async Task AgentChangesTheBehaviorInAJvmAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        PublishedAgent agent = PublishedAgent.Find("MyJvmAgent");
        string classPath = await jdk.CompileAsync("Fixtures/example/*.java", cancellationToken: TestContext.Current.CancellationToken);

        JavaRun without = await jdk.RunAsync("example.Target", classPath, cancellationToken: TestContext.Current.CancellationToken);
        JavaRun with = await jdk.RunAsync("example.Target", classPath, agent, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("RESULT=[a, b, c]", without.Output, StringComparison.Ordinal);
        Assert.Contains("RESULT=[c, b, a]", with.Output, StringComparison.Ordinal);
        Assert.Contains("patched example/Target", with.Output, StringComparison.Ordinal);
        Assert.Equal(expected: 0, with.ExitCode);
    }

    /// <summary>The same check through the jar that the publish writes beside the library, which a JVM loads with -javaagent. The test is skipped until the agent is published for this platform.</summary>
    [Theory]
    [JdkData(8, 17, 21)]
    public async Task AgentJarChangesTheBehaviorInAJvmAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        PublishedAgent agent = PublishedAgent.FindJar("MyJvmAgent");
        string classPath = await jdk.CompileAsync("Fixtures/example/*.java", cancellationToken: TestContext.Current.CancellationToken);

        JavaRun with = await jdk.RunAsync("example.Target", classPath, agent, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("RESULT=[c, b, a]", with.Output, StringComparison.Ordinal);
        Assert.Contains("patched example/Target", with.Output, StringComparison.Ordinal);
        Assert.Equal(expected: 0, with.ExitCode);
    }
}

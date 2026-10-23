using JvmBridge.Agents;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>Runs a class with added getters, setters and invokers on real JVMs with the verifier on every class.</summary>
public sealed class MemberVerificationTests
{
    /// <summary>The added methods verify, read and write the private fields, and call the private methods, of a javac-compiled class.</summary>
    /// <param name="jdk">The JDK to compile and run with.</param>
    [Theory]
    [JdkData(6, 8, 17)]
    public async Task AddedMembersVerifyAndRunAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classes = await jdk.CompileAsync(sources: "MemberFixtures/fixture/*.java", Math.Min(jdk.Major, val2: 8), TestContext.Current.CancellationToken);

        try
        {
            string subject = Path.Combine(classes, path2: "fixture", path3: "MemberSubject.class");

            ClassPatchResult result = ClassPatch.For(className: "fixture/MemberSubject")
                .AddFieldGetter(field: "name", name: "agent$name", returns: "Ljava/lang/Object;")
                .AddFieldSetter(field: "name", name: "agent$setName", accepts: "Ljava/lang/Object;")
                .AddFieldGetter(field: "count", name: "agent$count")
                .AddInvoker(method: "secret", descriptor: "(I)Ljava/lang/String;", name: "agent$secret")
                .AddInvoker(method: "twice", descriptor: "(I)I", name: "agent$twice")
                .Apply(await File.ReadAllBytesAsync(subject, TestContext.Current.CancellationToken));

            Assert.True(result.Changed, result.SkippedReason);
            Assert.Equal(expected: 5, result.AddedMembers);

            await File.WriteAllBytesAsync(subject, result.Bytes ?? [], TestContext.Current.CancellationToken);

            JavaRunOptions options = new();
            options.JvmArguments.Add(item: "-Xverify:all");

            JavaRun run = await jdk.RunAsync(
                mainClass: "fixture.MemberSubject",
                classPath: classes,
                options: options,
                cancellationToken: TestContext.Current.CancellationToken
            );

            Assert.Equal(expected: 0, run.ExitCode);
            Assert.Contains(expectedSubstring: "NAME=hidden COUNT=7 SECRET=changedchanged TWICE=42", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }
}

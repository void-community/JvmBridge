using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>Runs a class whose calls were redirected to helpers that take Object on real JVMs with the verifier on every class.</summary>
public sealed class RedirectVerificationTests
{
    /// <summary>A virtual and an interface receiver passed to helpers declared with Object pass the verifier and reach the helpers.</summary>
    [Theory]
    [JdkData(8, 17)]
    public async Task ObjectHelpersVerifyAndRunAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classes = await jdk.CompileAsync(sources: "RedirectFixtures/fixture/*.java", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            string subject = Path.Combine(classes, path2: "fixture", path3: "RedirectSubject.class");

            ClassPatchResult result = ClassPatch.For(className: "fixture/RedirectSubject")
                .Method(name: "measure", descriptor: "(Ljava/lang/String;Ljava/util/List;)I")
                .RedirectInvocation(
                    JavaMethod.Virtual(owner: "java/lang/String", name: "length", descriptor: "()I"),
                    JavaMethod.Static(owner: "fixture/ObjectHelpers", name: "length", descriptor: "(Ljava/lang/Object;)I")
                )
                .RedirectInvocation(
                    JavaMethod.Interface(owner: "java/util/List", name: "size", descriptor: "()I"),
                    JavaMethod.Static(owner: "fixture/ObjectHelpers", name: "size", descriptor: "(Ljava/lang/Object;)I")
                )
                .Apply(await File.ReadAllBytesAsync(subject, TestContext.Current.CancellationToken));

            Assert.True(result.Changed, result.SkippedReason);
            Assert.Equal(expected: 2, result.RedirectedInvocations);

            await File.WriteAllBytesAsync(subject, result.Bytes ?? [], TestContext.Current.CancellationToken);

            JavaRunOptions options = new();
            options.JvmArguments.Add(item: "-Xverify:all");

            JavaRun run = await jdk.RunAsync(
                mainClass: "fixture.RedirectSubject",
                classPath: classes,
                options: options,
                cancellationToken: TestContext.Current.CancellationToken
            );

            Assert.Equal(expected: 0, run.ExitCode);
            Assert.Contains(expectedSubstring: "RESULT=412", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }
}

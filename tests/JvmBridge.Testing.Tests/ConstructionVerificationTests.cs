using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>Runs classes after the construction and removal edits on real JVMs with the verifier on every class.</summary>
public sealed class ConstructionVerificationTests
{
    /// <summary>
    /// A field initializer constructs another class and another from a factory, a fluent chain loses its calls, and the class still verifies: the replaced fields hold the new objects, the field the patch does not name keeps its class, and the removed setters
    /// never ran.
    /// </summary>
    /// <param name="jdk">The JDK to compile and run with.</param>
    [Theory]
    [JdkData(6, 8, 17)]
    public async Task EditedConstructionsVerifyAndRunAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classes = await jdk.CompileAsync(sources: "ConstructionFixtures/fixture/*.java", Math.Min(jdk.Major, val2: 8), TestContext.Current.CancellationToken);

        try
        {
            string subject = Path.Combine(classes, path2: "fixture", path3: "ConstructionSubject.class");

            ClassPatchResult result = ClassPatch.For(className: "fixture/ConstructionSubject")
                .ReplaceConstruction(fromClass: "java/util/ArrayList", toClass: "java/util/concurrent/CopyOnWriteArrayList", storedIn: "sent")
                .ReplaceConstruction(
                    fromClass: "java/util/HashSet",
                    JavaMethod.Static(owner: "java/util/Collections", name: "emptySet", descriptor: "()Ljava/util/Set;"),
                    storedIn: "names"
                )
                .Method(name: "main", descriptor: "([Ljava/lang/String;)V")
                .RemoveInvocation(JavaMethod.Virtual(owner: "fixture/Config", name: "autosave", descriptor: "()Lfixture/Config;"))
                .RemoveInvocation(JavaMethod.Virtual(owner: "fixture/Config", name: "autoreload", descriptor: "()Lfixture/Config;"))
                .Apply(await File.ReadAllBytesAsync(subject, TestContext.Current.CancellationToken));

            Assert.True(result.Changed, result.SkippedReason);
            Assert.Equal(expected: 2, result.ReplacedConstructions);
            Assert.Equal(expected: 2, result.RemovedInvocations);

            await File.WriteAllBytesAsync(subject, result.Bytes ?? [], TestContext.Current.CancellationToken);

            JavaRunOptions options = new();
            options.JvmArguments.Add(item: "-Xverify:all");

            JavaRun run = await jdk.RunAsync(
                mainClass: "fixture.ConstructionSubject",
                classPath: classes,
                options: options,
                cancellationToken: TestContext.Current.CancellationToken
            );

            Assert.Equal(expected: 0, run.ExitCode);
            Assert.Contains(expectedSubstring: "SENT=java.util.concurrent.CopyOnWriteArrayList", run.Output, StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "OTHER=java.util.ArrayList", run.Output, StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "NAMES=java.util.Collections$EmptySet", run.Output, StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "FLAGS=false,false", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }
}

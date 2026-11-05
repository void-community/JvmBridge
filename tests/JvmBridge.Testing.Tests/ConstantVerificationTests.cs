using JvmBridge.Agents;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>
/// Replaces constants in one method of a class javac compiled, where another method and the static initializer load the same constants, as the patch for the early display window of Forge and NeoForge does, on real JVMs with the
/// verifier on every class.
/// </summary>
public sealed class ConstantVerificationTests
{
    /// <summary>initWindow loads the new long, int and String; render and the static initializer still load the old ones from the entries they share with it.</summary>
    /// <param name="jdk">The JDK that compiles the fixture and runs it.</param>
    [Theory]
    [JdkData(8, 17)]
    public async Task OnlyTheScopedMethodLoadsTheNewValuesAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classes = await jdk.CompileAsync(sources: "ConstantFixtures/fixture/*.java", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            string path = Path.Combine(classes, path2: "fixture", path3: "Window.class");

            ClassPatchResult result = ClassPatch.For(className: "fixture/Window")
                .Method(name: "initWindow", descriptor: "(Ljava/lang/String;)V")
                .ReplaceConstant(oldValue: 10L, newValue: 30L)
                .ReplaceConstant(oldValue: 100_000, newValue: 300_000)
                .ReplaceConstant(oldValue: "Timed out", newValue: "Gave up")
                .Apply(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

            Assert.Equal(expected: 3, result.ReplacedConstants);

            await File.WriteAllBytesAsync(path, result.Bytes ?? throw new InvalidOperationException(result.SkippedReason), TestContext.Current.CancellationToken);

            JavaRunOptions options = new();
            options.JvmArguments.Add(item: "-Xverify:all");

            JavaRun run = await jdk.RunAsync(mainClass: "fixture.Window", classPath: classes, options: options, cancellationToken: TestContext.Current.CancellationToken);
            string[] printed = ["init", "30", "300000", "Gave up", "10000000", "100000", "Timed out"];

            Assert.Equal(expected: 0, run.ExitCode);
            Assert.Equal(printed, run.Output.Split(separator: '\n').Select(static line => line.TrimEnd(trimChar: '\r')).Where(printed.Contains));
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }
}

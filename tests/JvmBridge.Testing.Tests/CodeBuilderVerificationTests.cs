using JvmBridge.ClassFiles;
using JvmBridge.ClassFiles.Code;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>Runs classes that <see cref="CodeBuilder"/> wrote on real JVMs with the verifier on every class, so the computed stack and local sizes and the branch offsets are the ones the JVM accepts.</summary>
public sealed class CodeBuilderVerificationTests
{
    private static readonly JavaMethod PrintInt = JavaMethod.Virtual(owner: "java/io/PrintStream", name: "println", descriptor: "(I)V");

    private static readonly JavaMethod PrintText = JavaMethod.Virtual(owner: "java/io/PrintStream", name: "println", descriptor: "(Ljava/lang/String;)V");

    /// <summary>A loop with branches in a class of version 50, and straight-line code in one of version 52, which the type-checking verifier checks, both verify and print what they compute.</summary>
    /// <param name="jdk">The JDK to run on.</param>
    [Theory]
    [JdkData(6, 8, 17)]
    public async Task BuiltClassesVerifyAndRunAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classes = Directory.CreateTempSubdirectory(prefix: "jvmbridge-built-").FullName;

        try
        {
            string package = Directory.CreateDirectory(Path.Combine(classes, path2: "fixture")).FullName;

            // for (int i = 1; i <= 10; i++) total += i; System.out.println(total);
            byte[] loop = new ClassFileWriter(name: "fixture/Loop", majorVersion: 50).AddMethod(name: "main", descriptor: "([Ljava/lang/String;)V", Sum).ToArray();

            // System.out.println("straight"); then the class of version 52 calls the loop.
            byte[] straight = new ClassFileWriter(name: "fixture/Straight")
                .AddMethod(
                    name: "main",
                    descriptor: "([Ljava/lang/String;)V",
                    static code => code.GetStatic(owner: "java/lang/System", name: "out", descriptor: "Ljava/io/PrintStream;")
                        .Ldc(value: "straight")
                        .Invoke(PrintText)
                        .ALoad(slot: 0)
                        .Invoke(JavaMethod.Static(owner: "fixture/Loop", name: "main", descriptor: "([Ljava/lang/String;)V"))
                        .Return()
                )
                .ToArray();

            await File.WriteAllBytesAsync(Path.Combine(package, path2: "Loop.class"), loop, TestContext.Current.CancellationToken);

            // Java 6 and 7 cannot load the class of version 52, so they run the loop on its own.
            if (jdk.Major >= 8)
                await File.WriteAllBytesAsync(Path.Combine(package, path2: "Straight.class"), straight, TestContext.Current.CancellationToken);

            JavaRunOptions options = new();
            options.JvmArguments.Add(item: "-Xverify:all");

            JavaRun run = await jdk.RunAsync(
                mainClass: jdk.Major >= 8 ? "fixture.Straight" : "fixture.Loop",
                classPath: classes,
                options: options,
                cancellationToken: TestContext.Current.CancellationToken
            );

            Assert.Equal(expected: 0, run.ExitCode);
            Assert.Contains(expectedSubstring: "55", run.Output, StringComparison.Ordinal);
            Assert.True(jdk.Major < 8 || run.Output.Contains(value: "straight", StringComparison.Ordinal), run.Output);
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    private static CodeBuilder Sum(CodeBuilder code)
    {
        CodeLabel loop = code.NewLabel();
        CodeLabel done = code.NewLabel();

        return code.IConst(value: 0)
            .IStore(slot: 1)
            .IConst(value: 1)
            .IStore(slot: 2)
            .Mark(loop)
            .ILoad(slot: 2)
            .IConst(value: 10)
            .If(Opcodes.IfICmpGt, done)
            .ILoad(slot: 1)
            .ILoad(slot: 2)
            .Op(Opcodes.IAdd)
            .IStore(slot: 1)
            .IInc(slot: 2, delta: 1)
            .Goto(loop)
            .Mark(done)
            .GetStatic(owner: "java/lang/System", name: "out", descriptor: "Ljava/io/PrintStream;")
            .ILoad(slot: 1)
            .Invoke(PrintInt)
            .Return();
    }
}

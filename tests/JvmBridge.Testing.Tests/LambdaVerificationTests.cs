using JvmBridge.ClassFiles;
using JvmBridge.ClassFiles.Code;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>
/// Runs lambdas that <see cref="CodeBuilder.Lambda"/> wrote into a class javac compiled, which has lambdas of its own, on real JVMs with the verifier on every class. Java 8 compiles a class of version 52, where a
/// private implementation needs REF_invokeSpecial, and Java 17 one of version 61, where it needs REF_invokeVirtual.
/// </summary>
public sealed class LambdaVerificationTests
{
    private const string Channels = "fixture/Channels";

    private static readonly JavaMethod Run = JavaMethod.Interface(owner: "java/lang/Runnable", name: "run", descriptor: "()V");

    /// <summary>The lambda javac wrote for <c>this::flush</c> is the one the builder writes for it: the same call site, bootstrap method and handle, so nothing is added to the class.</summary>
    /// <param name="jdk">The JDK whose javac writes the method reference.</param>
    [Theory]
    [JdkData(8, 17)]
    public async Task AJavacMethodReferenceIsReusedAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classes = await jdk.CompileAsync(sources: "LambdaFixtures/fixture/*.java", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            byte[] original = await File.ReadAllBytesAsync(Path.Combine(classes, path2: "fixture", path3: "Channels.class"), TestContext.Current.CancellationToken);
            JavaClassFile file = JavaClassFile.Read(original);
            CodeAttribute flusher = file.FindMethod(name: "flusher")?.Code ?? throw new InvalidOperationException(message: "Channels.flusher has no code.");
            Instruction call = flusher.Instructions.Single(static instruction => instruction.Opcode == Opcodes.InvokeDynamic);
            ClassFileEditor editor = new(original);

            byte[] assembled = editor.AssembleCode(static code => code.Lambda(Run, JavaMethod.Virtual(Channels, name: "flush", descriptor: "()V")));

            Assert.Equal(original.AsSpan(flusher.CodeOffset + call.Offset, call.Length).ToArray(), assembled);
            Assert.Equal(original, editor.ToArray());
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    /// <summary>
    /// <c>clear()</c> hands its moved body to the executor as a method reference and waits for it, as the patch for Minecraft's ChannelAccess does, and <c>describer()</c> returns a function that captures a
    /// string and lets LambdaMetafactory unbox the integer it is given. Both pass the verifier and run where they should.
    /// </summary>
    /// <param name="jdk">The JDK that compiles the fixture and runs it.</param>
    [Theory]
    [JdkData(8, 17)]
    public async Task LambdasVerifyAndRunAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classes = await jdk.CompileAsync(sources: "LambdaFixtures/fixture/*.java", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            string path = Path.Combine(classes, path2: "fixture", path3: "Channels.class");
            ClassFileEditor editor = new(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

            editor.ReplaceMethodBody(name: "clear", descriptor: "()V", movedBodyName: "agent$clear", ClearOnExecutor);
            editor.ReplaceMethodBody(name: "describer", descriptor: "()Ljava/util/function/Function;", movedBodyName: "agent$describer", Describer);

            bool IsMetafactory(MethodHandleEntry entry)
            {
                return editor.Model.GetMemberReference(entry.ReferenceIndex).Name == "metafactory";
            }

            Assert.Equal(expected: 1, editor.Model.ConstantPool.OfType<MethodHandleEntry>().Count(IsMetafactory));

            await File.WriteAllBytesAsync(path, editor.ToArray(), TestContext.Current.CancellationToken);

            JavaRunOptions options = new();
            options.JvmArguments.Add(item: "-Xverify:all");

            JavaRun run = await jdk.RunAsync(mainClass: "fixture.Channels", classPath: classes, options: options, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(expected: 0, run.ExitCode);
            Assert.Contains(expectedSubstring: "clear on Sound engine", run.Output, StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "release music", run.Output, StringComparison.Ordinal);
            Assert.Contains(expectedSubstring: "count 3", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    private static CodeBuilder ClearOnExecutor(CodeBuilder code)
    {
        const string futures = "java/util/concurrent/CompletableFuture";

        return code.ALoad(slot: 0)
            .Lambda(Run, JavaMethod.Virtual(Channels, name: "agent$clear", descriptor: "()V"))
            .ALoad(slot: 0)
            .GetField(Channels, name: "executor", descriptor: "Ljava/util/concurrent/ExecutorService;")
            .Invoke(
                JavaMethod.Static(
                    futures,
                    name: "runAsync",
                    descriptor: "(Ljava/lang/Runnable;Ljava/util/concurrent/Executor;)Ljava/util/concurrent/CompletableFuture;"
                )
            )
            .Invoke(JavaMethod.Virtual(futures, name: "join", descriptor: "()Ljava/lang/Object;"))
            .Pop()
            .Return();
    }

    private static CodeBuilder Describer(CodeBuilder code)
    {
        JavaMethod apply = JavaMethod.Interface(owner: "java/util/function/Function", name: "apply", descriptor: "(Ljava/lang/Object;)Ljava/lang/Object;");

        return code.Ldc(value: "count ").Lambda(apply, JavaMethod.Static(Channels, name: "describe", descriptor: "(Ljava/lang/String;I)Ljava/lang/String;")).AReturn();
    }
}

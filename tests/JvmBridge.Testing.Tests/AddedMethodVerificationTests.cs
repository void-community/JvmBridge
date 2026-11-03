using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>
/// Adds a helper method to a class javac compiled for Java 6 and calls it from two same-length <see cref="ClassFileEditor.ReplaceCode"/> edits, as the patch for Minecraft 1.7's
/// <c>PlayerControllerMP.updateController</c> does, on real JVMs with the verifier on every class.
/// </summary>
public sealed class AddedMethodVerificationTests
{
    private const string Controller = "fixture/Controller";

    private const string Update = "updateController";

    private static readonly JavaMethod IsOpen = JavaMethod.Virtual(owner: "fixture/Connection", name: "isChannelOpen", descriptor: "()Z");

    private static readonly JavaMethod Process = JavaMethod.Virtual(owner: "fixture/Connection", name: "processReceivedPackets", descriptor: "()V");

    private static readonly JavaMethod Helper = JavaMethod.Static(Controller, name: "agent$processReceivedPackets", descriptor: "(Lfixture/Connection;)Z");

    /// <summary>
    /// The tick handles the received packets once before it checks the connection, so the chat that came just before the disconnect is shown: <c>isChannelOpen()</c> becomes a call of the added
    /// helper, which handles the packets and then checks, and the handling in the open branch becomes a <c>pop</c> of the connection.
    /// </summary>
    /// <param name="jdk">The JDK that compiles the fixture for Java 6 and runs it.</param>
    [Theory]
    [JdkData(8, 11)]
    public async Task AnAddedMethodVerifiesAndRunsAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string classes = await jdk.CompileAsync(sources: "AddedMethodFixtures/fixture/*.java", release: 6, TestContext.Current.CancellationToken);

        try
        {
            JavaRunOptions options = new();
            options.JvmArguments.Add(item: "-Xverify:all");

            JavaRun before = await jdk.RunAsync(mainClass: "fixture.Controller", classPath: classes, options: options, cancellationToken: TestContext.Current.CancellationToken);

            Assert.DoesNotContain(expectedSubstring: "chat goodbye", before.Output, StringComparison.Ordinal);

            string path = Path.Combine(classes, path2: "fixture", path3: "Controller.class");
            ClassFileEditor editor = new(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

            Assert.Equal(expected: 50, editor.Model.MajorVersion);

            editor.AddMethod(
                accessFlags: 0x100A,
                Helper.Name,
                Helper.Descriptor,
                static code => code.ALoad(slot: 0).Invoke(Process).ALoad(slot: 0).Invoke(IsOpen).IReturn()
            );

            Assert.True(
                Replace(editor, editor.AssembleCode(static code => code.Invoke(IsOpen)), editor.AssembleCode(static code => code.Invoke(Helper)))
            );
            Assert.True(
                Replace(editor, editor.AssembleCode(static code => code.Invoke(Process)), editor.AssembleCode(static code => code.Pop().Nop().Nop()))
            );

            await File.WriteAllBytesAsync(path, editor.ToArray(), TestContext.Current.CancellationToken);

            JavaRun after = await jdk.RunAsync(mainClass: "fixture.Controller", classPath: classes, options: options, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(expected: 0, after.ExitCode);
            Assert.Contains(expectedSubstring: "chat hello", after.Output, StringComparison.Ordinal);
            int goodbye = after.Output.IndexOf(value: "chat goodbye", StringComparison.Ordinal);

            Assert.InRange(goodbye, low: 0, after.Output.IndexOf(value: "disconnected", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    // Replaces the one instruction of updateController whose bytes are the expected ones.
    private static bool Replace(ClassFileEditor editor, byte[] expected, byte[] replacement)
    {
        CodeAttribute code = editor.Model.FindMethod(Update)?.Code ?? throw new InvalidOperationException(message: "updateController has no code.");
        byte[] bytes = editor.ToArray();
        Instruction instruction = code.Instructions.Single(candidate => bytes.AsSpan(code.CodeOffset + candidate.Offset, candidate.Length).SequenceEqual(expected));

        return editor.ReplaceCode(Update, methodDescriptor: null, instruction.Offset, expected, replacement);
    }
}

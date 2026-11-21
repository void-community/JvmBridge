using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>Runs advised classes on real JVMs with the verifier on every class, which is what proves that the stubs are valid bytecode for every parameter and result type.</summary>
public sealed class AdviceVerificationTests
{
    private const string Hooks = "fixture/Hooks";
    private const string Subject = "fixture/AdviceSubject";

    private static ClassPatch Advice
    {
        get
        {
            const string self = "Ljava/lang/Object;";

            return ClassPatch.For(Subject)
                .Method(name: "staticInt", descriptor: "(IJ)I")
                .Before(Hook(name: "beforeStaticInt", descriptor: $"({self}IJ)V"))
                .After(Hook(name: "afterStaticInt", descriptor: $"({self}I)I"))
                .Method(name: "staticLong", descriptor: "(DLjava/lang/String;)J")
                .After(Hook(name: "afterStaticLong", descriptor: $"({self}DLjava/lang/String;J)J"), includeArguments: true)
                .Method(name: "staticDouble", descriptor: "(F[ILjava/lang/Object;)D")
                .Before(Hook(name: "beforeStaticDouble", descriptor: $"({self}F[I{self})V"))
                .Method(name: "staticVoid", descriptor: "(Ljava/lang/StringBuilder;)V")
                .Before(Hook(name: "beforeStaticVoid", descriptor: $"({self}Ljava/lang/StringBuilder;)V"))
                .After(Hook(name: "afterStaticVoid", descriptor: $"({self})V"))
                .Method(name: "staticString", descriptor: "(Ljava/lang/String;)Ljava/lang/String;")
                .After(Hook(name: "afterStaticString", descriptor: $"({self}Ljava/lang/String;)Ljava/lang/String;"))
                .Method(name: "staticArray", descriptor: "(I)[I")
                .After(Hook(name: "afterStaticArray", descriptor: $"({self}[I)[I"))
                .Method(name: "instanceInt", descriptor: "(I)I")
                .Before(Hook(name: "beforeInstanceInt", descriptor: "(Lfixture/AdviceSubject;I)V"))
                .After(Hook(name: "afterInstanceInt", descriptor: $"({self}I)I"))
                .Method(name: "instanceLong", descriptor: "(JJ)J")
                .After(Hook(name: "afterInstanceLong", descriptor: $"({self}JJJ)J"), includeArguments: true)
                .Method(name: "instanceString", descriptor: "(Ljava/lang/String;CZBS)Ljava/lang/String;")
                .Before(Hook(name: "beforeInstanceString", descriptor: $"({self}Ljava/lang/String;CZBS)V"))
                .Method(name: "instanceVoid", descriptor: "(Ljava/lang/String;)V")
                .Before(Hook(name: "beforeInstanceVoid", descriptor: $"({self}Ljava/lang/String;)V"))
                .After(Hook(name: "afterInstanceVoid", descriptor: $"({self})V"))
                .Method(name: "syncMethod", descriptor: "(I)I")
                .Before(Hook(name: "beforeSyncMethod", descriptor: $"({self}I)V"))
                .After(Hook(name: "afterSyncMethod", descriptor: $"({self}I)I"))
                .Method(name: "withTry", descriptor: "(I)I")
                .After(Hook(name: "afterWithTry", descriptor: $"({self}I)I"))
                .Method(name: "annotated", descriptor: "(I)I")
                .After(Hook(name: "afterAnnotated", descriptor: $"({self}I)I"))
                .Method(name: "thrower", descriptor: "(I)I")
                .Before(Hook(name: "beforeThrower", descriptor: $"({self}I)V"))
                .After(Hook(name: "afterThrower", descriptor: $"({self}I)I"));
        }
    }

    /// <summary>Every kind of parameter and result, instance and static, plain and synchronized, with and without exception handlers, passes the verifier and behaves as the hooks say.</summary>
    /// <param name="jdk">The JDK that compiles the fixture and runs it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="jdk"/> is <see langword="null"/>.</exception>
    [Theory]
    [JdkData(8, 17)]
    public async Task AdvisedMethodsVerifyAndRunAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);

        string classes = await jdk.CompileAsync(sources: "AdviceFixtures/fixture/*.java", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            string subjectFile = Path.Combine(classes, path2: "fixture", path3: "AdviceSubject.class");
            ClassPatchResult patched = Advice.Apply(await File.ReadAllBytesAsync(subjectFile, TestContext.Current.CancellationToken));

            Assert.True(patched.Changed, patched.SkippedReason);
            Assert.Equal(expected: 14, patched.AdvisedMethods);

            await File.WriteAllBytesAsync(subjectFile, patched.Bytes ?? [], TestContext.Current.CancellationToken);

            JavaRunOptions options = new();

            options.JvmArguments.Add(item: "-Xverify:all");

            JavaRun run = await jdk.RunAsync(mainClass: "fixture.AdviceMain",
                classPath: classes,
                options: options,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(expected: 0, run.ExitCode);

            string[] lines = [.. run.Output.Split(separator: '\n').Select(static line => line.TrimEnd(trimChar: '\r'))];

            // Static methods run their hooks around an int, a long, a double and a void result.
            Assert.Contains(expected: "staticInt=1007", lines);
            Assert.Contains(expected: "staticLong=10", lines);
            Assert.Contains(expected: "staticDouble=3.5", lines);
            Assert.Contains(expected: "staticVoid=v", lines);

            // A reference and an array go through the after hooks like the primitive results do.
            Assert.Contains(expected: "staticString=x!?", lines);
            Assert.Contains(expected: "staticArray=4", lines);

            // Instance methods are advised the same way, with the receiver in front of the arguments.
            Assert.Contains(expected: "instanceInt=9", lines);
            Assert.Contains(expected: "instanceLong=27", lines);
            Assert.Contains(expected: "instanceString=sctrue12", lines);

            // A synchronized method, one with exception handlers and an annotated one keep what javac gave them.
            Assert.Contains(expected: "syncMethod=42", lines);
            Assert.Contains(expected: "withTry=5,-1", lines);
            Assert.Contains(expected: "annotated=10 deprecated=true", lines);

            // The original code sits in a method of its own, which is the frame an exception it throws starts from.
            Assert.Contains(expected: "hasBody=true", lines);
            Assert.Contains(expected: "thrower=thrown 4 frames=thrower$jvmbridge$body,thrower", lines);

            Assert.Equal(expected:
            [
                "log b:staticInt:null:3:4",
                "log b:staticDouble:1.5:2:o",
                "log b:staticVoid:0",
                "log a:staticVoid",
                "log b:instanceInt:1:7",
                "log b:instanceString:sctrue12",
                "log b:instanceVoid:arg",
                "log body:arg",
                "log a:instanceVoid",
                "log b:sync:true",
                "log a:sync:true",
                "log finally",
                "log a:withTry:5",
                "log finally",
                "log a:withTry:-1",
                "log b:thrower",
            ],
                lines.Where(static line => line.StartsWith(value: "log ", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(classes, recursive: true);
        }
    }

    private static JavaMethod Hook(string name, string descriptor)
    {
        return JavaMethod.Static(Hooks, name, descriptor);
    }
}

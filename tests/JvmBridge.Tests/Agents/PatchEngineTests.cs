using System.Collections.Concurrent;

using JvmBridge.Agents;
using JvmBridge.ClassFiles;
using JvmBridge.Tests.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.Agents;

/// <summary>Verifies how the engine applies, orders and removes patches as classes load, without a JVM.</summary>
[Collection(FakeJvm.CollectionName)]
public sealed class PatchEngineTests
{
    private static byte[] Annotated => ClassFileFixtures.Read(resource: "ClassFiles.Annotated.class");

    /// <summary>Registering a patch twice keeps one copy, so its edits run once per class.</summary>
    [Fact]
    public void ARegisteredPatchStaysOnce()
    {
        using FakeJvm jvm = new();

        AgentContext context = jvm.CreateContext(name: "once");
        int runs = 0;
        ClassPatch patch = ClassPatch.For(className: "fixture/Sample").Edit(editor => runs++);

        context.Patches.Add(patch);
        context.Patches.Add(patch);
        byte[]? result = Transform(context, name: "fixture/Sample", ClassFileFixtures.Sample);

        Assert.NotNull(result);
        Assert.Equal(expected: 1, runs);
    }

    /// <summary>Patches apply in the order they were registered, whether they name a class or match classes, and each sees the previous one's output.</summary>
    [Fact]
    public void AppliesPatchesInRegistrationOrder()
    {
        using FakeJvm jvm = new();

        AgentContext matchingFirst = jvm.CreateContext(name: "matching-first");
        matchingFirst.Patch(ClassPatch.ForPackage(package: "fixture").ReplaceConstant(oldValue: "hello", newValue: "one"));
        matchingFirst.Patch(ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "one", newValue: "two"));
        AgentContext namedFirst = jvm.CreateContext(name: "named-first");
        namedFirst.Patch(ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "one", newValue: "two"));
        namedFirst.Patch(ClassPatch.ForPackage(package: "fixture").ReplaceConstant(oldValue: "hello", newValue: "one"));

        string[] chained = Strings(Transform(matchingFirst, name: "fixture/Sample", ClassFileFixtures.Sample));
        string[] skipped = Strings(Transform(namedFirst, name: "fixture/Sample", ClassFileFixtures.Sample));

        Assert.Contains(expected: "two", chained);
        Assert.DoesNotContain(expected: "hello", chained);
        Assert.Contains(expected: "one", skipped);
        Assert.DoesNotContain(expected: "two", skipped);
    }

    /// <summary>A patched class needs only the loader helpers it mentions: one that mentions none is patched even where no helper can be defined (the bootstrap loader, here a missing JNI), one that mentions a helper is skipped with the reason.</summary>
    [Fact]
    public void BootstrapClassesNeedOnlyTheHelpersTheyMention()
    {
        ConcurrentQueue<LogLine> lines = [];
        AgentLogSink previous = AgentLog.Sink;

        try
        {
            AgentLog.Sink = (level, source, message) => lines.Enqueue(new LogLine(level, source, message));

            using FakeJvm jvm = new();

            AgentContext context = jvm.CreateContext(name: "bootstrap");
            JavaClassDefinition helper = JavaClassDefinition.FromBytes(new ClassFileWriter(name: "jvmbridge/test/LoaderHelper").ToArray());

            context.DefineClass(helper, ClassLoaderTarget.PatchedClassLoader);
            context.Patches.Add(ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "hello", newValue: "bye"));

            byte[]? independent = Transform(context, name: "fixture/Sample", ClassFileFixtures.Sample);

            AgentContext mentioning = jvm.CreateContext(name: "mentioning");

            mentioning.DefineClass(helper, ClassLoaderTarget.PatchedClassLoader);
            mentioning.Patches.Add(ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "hello", newValue: "jvmbridge/test/LoaderHelper"));

            byte[]? dependent = Transform(mentioning, name: "fixture/Sample", ClassFileFixtures.Sample);

            Assert.Contains(expected: "bye", Strings(independent));
            Assert.Null(dependent);
            Assert.Contains(
                lines,
                line => line.Message.Contains(value: "skipped fixture/Sample: helper jvmbridge/test/LoaderHelper", StringComparison.Ordinal)
            );
        }
        finally
        {
            AgentLog.Sink = previous;
        }
    }

    /// <summary>Classes that no patch targets are left alone, and the supertypes of classes are only learned when a patch asks for them.</summary>
    [Fact]
    public void LearnsSupertypesOnlyWhenAPatchNeedsThem()
    {
        using FakeJvm jvm = new();

        AgentContext plain = jvm.CreateContext(name: "plain");
        plain.Patch(ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "hello", newValue: "bye"));
        AgentContext hierarchical = jvm.CreateContext(name: "hierarchical");
        hierarchical.Patch(ClassPatch.ForSubclassesOf(superType: "java/lang/Runnable").ReplaceConstant(oldValue: "item", newValue: "entry"));

        byte[]? untouched = Transform(plain, name: "fixture/Annotated", Annotated);
        byte[]? learned = Transform(hierarchical, name: "fixture/Annotated", Annotated);

        Assert.Null(untouched);
        Assert.False(plain.Patches.Hierarchy.Contains(className: "fixture/Annotated"));
        Assert.Contains(expected: "entry", Strings(learned));
        Assert.True(hierarchical.Patches.Hierarchy.Contains(className: "fixture/Annotated"));
    }

    /// <summary>A patch that matches many classes logs only what it changed, at information level, and its skips at debug level; a patch that names its class logs every outcome.</summary>
    [Fact]
    public void LogsMatchingPatchesQuietly()
    {
        ConcurrentQueue<LogLine> lines = [];
        AgentLogSink previous = AgentLog.Sink;

        try
        {
            AgentLog.Sink = (level, source, message) => lines.Enqueue(new LogLine(level, source, message));

            using FakeJvm jvm = new();

            string name = "quiet-" + Guid.NewGuid().ToString(format: "N");
            AgentContext context = jvm.CreateContext(name);
            context.LogLevel = AgentLogLevel.Debug;
            context.Patch(
                ClassPatch.ForPackage(package: "fixture").Method(name: "absent").RedirectInvocation(JavaMethod.Static(owner: "a/B", name: "c", descriptor: "()V"), toOwner: "x/Y")
            );
            context.Patch(
                ClassPatch.For(className: "fixture/Sample").Method(name: "absent").RedirectInvocation(JavaMethod.Static(owner: "a/B", name: "c", descriptor: "()V"), toOwner: "x/Y")
            );

            byte[]? result = Transform(context, name: "fixture/Sample", ClassFileFixtures.Sample);
            LogLine[] own = [.. lines.Where(line => line.Source == name)];

            Assert.Null(result);
            Assert.Equal(
                expected: 1,
                own.Count(line => line.Level == AgentLogLevel.Debug && line.Message.StartsWith(value: "skipped fixture/Sample", StringComparison.Ordinal))
            );
            Assert.Equal(
                expected: 1,
                own.Count(
                    line => line.Level == AgentLogLevel.Information && line.Message.StartsWith(value: "skipped fixture/Sample", StringComparison.Ordinal)
                )
            );
        }
        finally
        {
            AgentLog.Sink = previous;
        }
    }

    /// <summary>A removed patch no longer applies, removing it twice says it was not registered, and a patch that was never registered is a no-op.</summary>
    [Fact]
    public void RemovedPatchesStopApplying()
    {
        using FakeJvm jvm = new();

        AgentContext context = jvm.CreateContext(name: "remove");
        ClassPatch patch = ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "hello", newValue: "bye");
        context.Patch(patch);

        byte[]? before = Transform(context, name: "fixture/Sample", ClassFileFixtures.Sample);
        PatchReport removed = context.Unpatch(patch);
        byte[]? after = Transform(context, name: "fixture/Sample", ClassFileFixtures.Sample);
        PatchReport again = context.Unpatch(patch);

        Assert.NotNull(before);
        Assert.Null(after);
        Assert.Contains(expectedSubstring: "retransformation was not enabled", removed.NotRetransformedReason, StringComparison.Ordinal);
        Assert.Equal(expected: "the patch was not registered", again.NotRetransformedReason);
    }

    /// <summary>Before events are enabled a patch is only registered, and afterwards a report says why no loaded class was retransformed when the capability is missing.</summary>
    [Fact]
    public void ReportsWhyLoadedClassesWereNotRetransformed()
    {
        using FakeJvm jvm = new();

        AgentContext context = jvm.CreateContext(name: "report");
        ClassPatch early = ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "hello", newValue: "early");
        ClassPatch late = ClassPatch.For(className: "fixture/Sample").ReplaceConstant(oldValue: "early", newValue: "late");

        PatchReport registered = context.PatchNow(early);
        context.EventsEnabled = true;
        PatchReport afterEvents = context.PatchNow(late);

        Assert.Contains(expectedSubstring: "registered", registered.NotRetransformedReason, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "TryEnableRetransformation", afterEvents.NotRetransformedReason, StringComparison.Ordinal);
        Assert.Empty(afterEvents.Classes);
        Assert.Contains(expected: "late", Strings(Transform(context, name: "fixture/Sample", ClassFileFixtures.Sample)));
    }

    private static string[] Strings(byte[]? bytes)
    {
        Assert.NotNull(bytes);

        return JavaClassFile.Read(bytes).GetStrings();
    }

    private static byte[]? Transform(AgentContext context, string name, byte[] bytes)
    {
        ClassFile file = new(name, isRetransformation: false, bytes);
        ClassTransformContext callback = new(environment: null, loader: 0, protectionDomain: 0, classBeingRedefined: 0, canUseJni: false);

        return context.Patches.Transform(file, callback);
    }

    private sealed record LogLine(AgentLogLevel Level, string Source, string Message);
}

using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Tests.Agents;

/// <summary>Verifies what ExportPackage and OpenPackage accept, without a JVM.</summary>
[Collection(FakeJvm.CollectionName)]
public sealed class ModuleAccessTests
{
    private static readonly JavaClassDefinition Nested = JavaClassDefinition.FromBytes(new ClassFileWriter(name: "test/Channels$Reopening").ToArray());

    private static readonly JavaClassDefinition Helper = new(name: "test/Channels", new ClassFileWriter(name: "test/Channels").ToArray(), [Nested]);

    /// <summary>A package goes to a helper of the agent, named by the helper or one of its nested classes, in internal form or with dots.</summary>
    [Fact]
    public void ExportsToAHelper()
    {
        using FakeJvm jvm = new();

        AgentContext context = jvm.CreateContext(name: "exports");

        context.DefineClass(Helper, ClassLoaderTarget.Bootstrap);

        Exception? failure = Record.Exception(
            () =>
            {
                context.ExportPackage(module: "java.base", package: "sun/nio/ch", Helper);
                context.ExportPackage(module: "java.base", package: "sun/nio/ch", Helper);
                context.OpenPackage(module: "java.base", package: "java.lang.invoke", Nested);
            }
        );

        Assert.Null(failure);
    }

    /// <summary>A class that is not a helper of the agent, a malformed package and a call after Configure are refused.</summary>
    [Fact]
    public void RefusesWhatCannotBeGranted()
    {
        using FakeJvm jvm = new();

        AgentContext context = jvm.CreateContext(name: "refused");

        InvalidOperationException undeclared = Assert.Throws<InvalidOperationException>(() => context.ExportPackage(module: "java.base", package: "sun/nio/ch", Helper));
        context.DefineClass(Helper, ClassLoaderTarget.PatchedClassLoader);
        ArgumentException malformed = Assert.Throws<ArgumentException>(() => context.OpenPackage(module: "java.base", package: "sun//ch", Helper));
        ArgumentException blank = Assert.Throws<ArgumentException>(() => context.ExportPackage(module: " ", package: "sun/nio/ch", Helper));
        context.EventsEnabled = true;
        InvalidOperationException late = Assert.Throws<InvalidOperationException>(() => context.ExportPackage(module: "java.base", package: "sun/nio/ch", Helper));

        Assert.Equal(expected: "test/Channels is not a helper of this agent; declare it with DefineClass first.", undeclared.Message);
        Assert.StartsWith(expectedStartString: "sun//ch is not a package name.", malformed.Message, StringComparison.Ordinal);
        Assert.Equal(expected: "module", blank.ParamName);
        Assert.Contains(expectedSubstring: "from Configure", late.Message, StringComparison.Ordinal);
    }
}

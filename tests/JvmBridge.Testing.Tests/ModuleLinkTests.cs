using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;

namespace JvmBridge.Testing.Tests;

/// <summary>Verifies that the link check follows the packages that JDK modules export.</summary>
public sealed class ModuleLinkTests
{
    // static long size(FileChannel channel) { return ((FileChannelImpl) channel).size(); }
    private static readonly JavaClassDefinition Channels = JavaClassDefinition.FromBytes(
        new ClassFileWriter(name: "agent/Channels")
            .AddMethod(
                name: "size",
                descriptor: "(Ljava/nio/channels/FileChannel;)J",
                static code => code.ALoad(slot: 0).CheckCast(className: "sun/nio/ch/FileChannelImpl").Invoke(JavaMethod.Virtual(owner: "sun/nio/ch/FileChannelImpl", name: "size", descriptor: "()J")).LReturn()
            )
            .ToArray()
    );

    /// <summary>From Java 9, a helper that names a class of a package its module does not export links only with that package exported to it; Java 8 has no modules.</summary>
    /// <param name="jdk">The JDK whose classes the helper uses.</param>
    [Theory]
    [JdkData(8, 17)]
    public async Task UnexportedPackagesNeedAnExportAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await ClassFileAssert.LinksAsync(jdk, Channels, [], [], ["java.base/sun.nio.ch"], cancellationToken);
        Exception? unexported = await Record.ExceptionAsync(() => ClassFileAssert.LinksAsync(jdk, Channels, classPath: [], cancellationToken: cancellationToken));

        if (jdk.Major < 9)
        {
            Assert.Null(unexported);

            return;
        }

        Assert.NotNull(unexported);
        Assert.Contains(
            expectedSubstring: "agent/Channels uses sun/nio/ch/FileChannelImpl, but java.base does not export sun/nio/ch",
            unexported.Message,
            StringComparison.Ordinal
        );

        string line = Assert.Single(
            unexported.Message.Split(Environment.NewLine),
            static candidate => candidate.Contains(value: "does not export", StringComparison.Ordinal)
        );

        Assert.EndsWith(expectedEndString: "pass java.base/sun/nio/ch as an exported package here", line, StringComparison.Ordinal);
    }
}

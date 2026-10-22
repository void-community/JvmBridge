using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

using Xunit;

namespace JvmBridge.Tasks.Tests;

/// <summary>Verifies the inventory lookup that the JDK provisioning task embeds. The task itself is exercised by the package-consumer builds in JvmBridge.Build.Tests.</summary>
public sealed class PinnedJdkTests
{
    /// <summary>A Java version the inventory lacks has no entry, which the task reports as an unsupported JDK.</summary>
    [Fact]
    public void FindsNothingForAJavaVersionTheInventoryLacks()
    {
        Assert.Null(PinnedJdk.Find(major: 7, implementation: "hotspot", runtimeIdentifier: "linux-x64"));
    }

    /// <summary>An implementation the inventory does not know has no entry.</summary>
    [Fact]
    public void FindsNothingForAnUnknownImplementation()
    {
        Assert.Null(PinnedJdk.Find(major: 17, implementation: "unknown", runtimeIdentifier: "linux-x64"));
    }

    /// <summary>The inventory embedded in the task assembly has the Java 17 JDK that the template builds with, with the checksum it is verified against.</summary>
    [Fact]
    public void FindsTheJava17Jdk()
    {
        JdkEntry? entry = PinnedJdk.Find(major: 17, implementation: "hotspot", runtimeIdentifier: "linux-x64");

        Assert.NotNull(entry);
        Assert.Equal(expected: "available", entry.Status);
        Assert.Equal(expected: 64, entry.Sha256?.Length);
    }
}

using System.Runtime.InteropServices;

using JvmBridge.Build.Infrastructure;

namespace JvmBridge.IntegrationTests;

/// <summary>
/// The target whose published samples the tests run. On a developer's machine TARGET_RID names it, once the samples are published for it as the README describes. CI publishes the samples for every runtime and
/// runs the tests once on each, so there the target is the runtime that the tests themselves run on.
/// </summary>
internal static class TestTarget
{
    /// <summary>The image with glibc 2.17, the floor of an agent linked against the sysroot of .NET's cross-build image, that CI runs such an agent in.</summary>
    internal const string GlibcImage = "centos:7@sha256:be65f488b7764ad3638f236b7b515b3678369a5124c47b8d32916d6487418ea4";

    /// <summary>Gets whether CI published the samples for the runtime that the tests run on, which it hands to the tests in artifacts/publish-&lt;runtime&gt;.</summary>
    public static bool PublishedByCi => Directory.Exists(Path.Combine(RepositoryContext.Discover().Artifacts, "publish-" + RuntimeInformation.RuntimeIdentifier));

    /// <summary>Gets the runtime identifier of the target.</summary>
    public static string Rid => Environment.GetEnvironmentVariable(variable: "TARGET_RID") ?? RuntimeInformation.RuntimeIdentifier;

    /// <summary>Gets whether a target is named or CI published the samples for this one.</summary>
    public static bool Selected => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable: "TARGET_RID")) || PublishedByCi;
}

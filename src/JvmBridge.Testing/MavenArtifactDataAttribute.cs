using System.Reflection;

using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace JvmBridge.Testing;

/// <summary>
/// Supplies a checksum-pinned <see cref="MavenArtifact"/> to a theory, one row per attribute, for example <c>[MavenArtifactData("https://maven.minecraftforge.net", "net.minecraftforge:eventbus:0.10.3", "…")]</c> for each version of an application a patch
/// must change or skip. The file is downloaded when a test asks for its path.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class MavenArtifactDataAttribute : DataAttribute
{
    /// <summary>Pins an artifact for the theory.</summary>
    /// <param name="repository">The repository's base address, for example <c>https://repo1.maven.org/maven2</c>.</param>
    /// <param name="coordinates"><c>group:artifact:version</c>, with an optional <c>:classifier</c> and <c>@extension</c>.</param>
    /// <param name="sha256">The SHA-256 of the file, in hexadecimal.</param>
    public MavenArtifactDataAttribute(string repository, string coordinates, string sha256)
    {
        Repository = repository;
        Coordinates = coordinates;
        Sha256 = sha256;
    }

    /// <summary>Gets the <c>group:artifact:version</c> of the artifact.</summary>
    public string Coordinates { get; }

    /// <summary>Gets the repository's base address.</summary>
    public string Repository { get; }

    /// <summary>Gets the pinned SHA-256 of the file.</summary>
    public string Sha256 { get; }

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        ITheoryDataRow[] rows = [new TheoryDataRow<MavenArtifact>(new MavenArtifact(Repository, Coordinates, Sha256)) { TestDisplayName = Coordinates }];

        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc/>
    public override bool SupportsDiscoveryEnumeration()
    {
        return true;
    }
}

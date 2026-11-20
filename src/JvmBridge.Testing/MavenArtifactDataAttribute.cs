using System.Reflection;

using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace JvmBridge.Testing;

/// <summary>
/// Supplies a checksum-pinned <see cref="MavenArtifact"/> to a theory, one row per attribute, for example <c>[MavenArtifactData("https://maven.minecraftforge.net", "net.minecraftforge:eventbus:0.10.3", "…")]</c> for each version of an application a patch
/// must change or skip. The file is downloaded when a test asks for its path.
/// </summary>
/// <remarks>Initializes a new instance of the <see cref="MavenArtifactDataAttribute"/> class. Pins an artifact for the theory.</remarks>
/// <param name="repository">The repository's base address, for example <c>https://repo1.maven.org/maven2</c>.</param>
/// <param name="coordinates"><c>group:artifact:version</c>, with an optional <c>:classifier</c> and <c>@extension</c>.</param>
/// <param name="sha256">The SHA-256 of the file, in hexadecimal.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class MavenArtifactDataAttribute(string repository, string coordinates, string sha256) : DataAttribute
{
    /// <summary>Gets the <c>group:artifact:version</c> of the artifact.</summary>
    /// <value>The coordinates as the attribute was given them, which also name the test row.</value>
    public string Coordinates { get; } = coordinates;

    /// <summary>Gets the repository's base address.</summary>
    /// <value>The address without the path of the artifact, for example <c>https://repo1.maven.org/maven2</c>.</value>
    public string Repository { get; } = repository;

    /// <summary>Gets the pinned SHA-256 of the file.</summary>
    /// <value>Sixty-four hexadecimal digits, in either case.</value>
    public string Sha256 { get; } = sha256;

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        ITheoryDataRow[] rows = [new TheoryDataRow<MavenArtifact>(new MavenArtifact(Repository, Coordinates, Sha256))
        {
            TestDisplayName = Coordinates
        }];

        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc/>
    public override bool SupportsDiscoveryEnumeration()
    {
        return true;
    }
}

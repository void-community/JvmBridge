using System.Reflection;

using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace JvmBridge.Testing;

/// <summary>
/// Supplies a <see cref="PinnedArtifact"/> at an address, pinned by its checksum, to a theory, one row per attribute, for example <c>[PinnedArtifactData("https://launcher.mojang.com/v1/objects/&lt;sha1&gt;/server.jar", "…", FileName =
/// "server-1.12.2.jar")]</c> for each release a patch must change or skip. The file is downloaded when a test asks for its path.
/// </summary>
/// <remarks>Initializes a new instance of the <see cref="PinnedArtifactDataAttribute"/> class. Pins a file for the theory.</remarks>
/// <param name="address">The absolute HTTP or HTTPS address of the file.</param>
/// <param name="sha256">The SHA-256 of the file, in hexadecimal.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PinnedArtifactDataAttribute(string address, string sha256) : DataAttribute
{
    /// <summary>Gets the address of the file.</summary>
    /// <value>The absolute HTTP or HTTPS address as the attribute was given it.</value>
    public string Address { get; } = address;

    /// <summary>Gets the pinned SHA-256 of the file.</summary>
    /// <value>Sixty-four hexadecimal digits, in either case.</value>
    public string Sha256 { get; } = sha256;

    /// <summary>Gets or sets the name of the cached file, which also names the test row; the last segment of the address when it is not set.</summary>
    /// <value>A file name without a directory; <see langword="null"/> by default.</value>
    public string? FileName { get; set; }

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        PinnedArtifact artifact = new(new Uri(Address), Sha256, FileName);

        ITheoryDataRow[] rows = [new TheoryDataRow<PinnedArtifact>(artifact)
        {
            TestDisplayName = FileName ?? artifact.Url.AbsoluteUri
        }];

        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc/>
    public override bool SupportsDiscoveryEnumeration()
    {
        return true;
    }
}

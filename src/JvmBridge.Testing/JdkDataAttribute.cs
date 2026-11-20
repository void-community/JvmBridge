using System.Reflection;

using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace JvmBridge.Testing;

/// <summary>Supplies a <see cref="Jdk"/> for each Java major version to a theory, for example <c>[JdkData(8, 17)]</c>. The JDK is only resolved when a test uses it.</summary>
/// <remarks>Initializes a new instance of the <see cref="JdkDataAttribute"/> class. Lists the Java major versions the theory runs on.</remarks>
/// <param name="major">The first Java major version.</param>
/// <param name="additionalMajors">Further major versions, each run as its own test.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class JdkDataAttribute(int major, params int[] additionalMajors) : DataAttribute
{
    /// <summary>Gets the further Java major versions.</summary>
    /// <value>The versions after the first, in the order they were given; empty when the attribute names one version.</value>
    public IReadOnlyList<int> AdditionalMajors { get; } = additionalMajors;

    /// <summary>Gets or sets the JVM implementation, <c>hotspot</c> by default.</summary>
    /// <value><c>hotspot</c> or <c>openj9</c>, which every version the attribute lists is resolved with.</value>
    public string Implementation { get; init; } = "hotspot";

    /// <summary>Gets the first Java major version.</summary>
    /// <value>The version that the first row of the theory runs on, for example 8.</value>
    public int Major { get; } = major;

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        ITheoryDataRow[] rows = [.. new[] { Major }.Concat(AdditionalMajors).Select(CreateRow)];

        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc/>
    public override bool SupportsDiscoveryEnumeration()
    {
        return true;
    }

    private TheoryDataRow<Jdk> CreateRow(int version)
    {
        Jdk jdk = new(version, Implementation);

        return new TheoryDataRow<Jdk>(jdk)
        {
            TestDisplayName = jdk.ToString()
        };
    }
}

using System.Reflection;

using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace JvmBridge.Testing;

/// <summary>Supplies a <see cref="Jdk"/> for each Java major version to a theory, for example <c>[JdkData(8, 17)]</c>. The JDK is only resolved when a test uses it.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class JdkDataAttribute : DataAttribute
{
    /// <summary>Lists the Java major versions the theory runs on.</summary>
    /// <param name="major">The first Java major version.</param>
    /// <param name="additionalMajors">Further major versions, each run as its own test.</param>
    public JdkDataAttribute(int major, params int[] additionalMajors)
    {
        Major = major;
        AdditionalMajors = additionalMajors;
    }

    /// <summary>Gets the further Java major versions.</summary>
    public IReadOnlyList<int> AdditionalMajors { get; }

    /// <summary>Gets the JVM implementation, <c>hotspot</c> by default.</summary>
    public string Implementation { get; init; } = "hotspot";

    /// <summary>Gets the first Java major version.</summary>
    public int Major { get; }

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

        return new TheoryDataRow<Jdk>(jdk) { TestDisplayName = jdk.ToString() };
    }
}

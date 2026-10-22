using System.Globalization;

using JvmBridge.Agents;

using Xunit;

namespace JvmBridge.Tests.Agents;

/// <summary>Verifies that retransformation isolates the class the JVM refuses.</summary>
public sealed class RetransformBatchesTests
{
    /// <summary>A class the JVM refuses is found by splitting the chunk, carries the reason, and does not stop the others.</summary>
    [Fact]
    public void IsolatesTheFailingClass()
    {
        RecordingJvm jvm = new(failing: 37);

        List<PatchClassReport> reports = RetransformBatches.Run(Candidates(count: 120), jvm.Retransform);

        PatchClassReport failure = Assert.Single(reports, static report => !report.Succeeded);

        Assert.Equal(expected: "pkg/C37", failure.ClassName);
        Assert.Equal(expected: "JVMTI_ERROR_FAILS_VERIFICATION", failure.Failure);
        Assert.Equal(expected: 120, reports.Count);
        Assert.Equal(expected: 119, reports.Count(static report => report.Succeeded));
    }

    /// <summary>No candidates means no JVM call.</summary>
    [Fact]
    public void MakesNoCallWithoutCandidates()
    {
        RecordingJvm jvm = new(failing: 0);

        List<PatchClassReport> reports = RetransformBatches.Run([], jvm.Retransform);

        Assert.Empty(reports);
        Assert.Empty(jvm.BatchSizes);
    }

    /// <summary>When every class fails each one is reported with the reason.</summary>
    [Fact]
    public void ReportsEveryFailure()
    {
        List<PatchClassReport> reports = RetransformBatches.Run(Candidates(count: 5), RefuseEverything);

        Assert.Equal(expected: 5, reports.Count);
        Assert.All(reports, static report => Assert.Equal(expected: "JVMTI_ERROR_INVALID_CLASS", report.Failure));
    }

    /// <summary>When the JVM accepts everything, classes go in chunks of at most the chunk size and each is reported as retransformed.</summary>
    [Fact]
    public void RetransformsEverythingInChunks()
    {
        RecordingJvm jvm = new(failing: 0);

        List<PatchClassReport> reports = RetransformBatches.Run(Candidates(count: 250), jvm.Retransform);

        Assert.Equal([100, 100, 50], jvm.BatchSizes);
        Assert.Equal(expected: 250, reports.Count);
        Assert.All(reports, static report => Assert.True(report.Succeeded));
    }

    private static List<RetransformCandidate> Candidates(int count)
    {
        return [.. Enumerable.Range(start: 1, count).Select(static index => new RetransformCandidate("pkg/C" + index.ToString(CultureInfo.InvariantCulture), index))];
    }

    private static string? RefuseEverything(nint[] types)
    {
        return types.Length > 0 ? "JVMTI_ERROR_INVALID_CLASS" : null;
    }

    private sealed class RecordingJvm(nint failing)
    {
        internal List<int> BatchSizes { get; } = [];

        internal string? Retransform(nint[] types)
        {
            BatchSizes.Add(types.Length);

            return types.Contains(failing) ? "JVMTI_ERROR_FAILS_VERIFICATION" : null;
        }
    }
}

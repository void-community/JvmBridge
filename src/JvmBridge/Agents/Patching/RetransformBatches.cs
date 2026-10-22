namespace JvmBridge.Agents;

/// <summary>
/// Retransforms classes in chunks so that one class the JVM refuses does not stop the rest. A chunk the JVM rejects is split in halves until the failing class stands alone and carries the reason. Retransforming a class twice is harmless,
/// so the split is correct whether the JVM rejects a chunk atomically or only after changing some classes.
/// </summary>
internal static class RetransformBatches
{
    internal const int ChunkSize = 100;

    /// <summary>Retransforms every candidate and reports each.</summary>
    /// <param name="candidates">The class names and raw class references.</param>
    /// <param name="retransform">Retransforms a batch and returns the failure text, or <see langword="null"/> on success.</param>
    /// <returns>One report per candidate.</returns>
    internal static List<PatchClassReport> Run(IReadOnlyList<RetransformCandidate> candidates, Func<nint[], string?> retransform)
    {
        List<PatchClassReport> reports = [];

        for (int start = 0; start < candidates.Count; start += ChunkSize)
            Attempt([.. candidates.Skip(start).Take(ChunkSize)], retransform, reports);

        return reports;
    }

    private static void Attempt(RetransformCandidate[] group, Func<nint[], string?> retransform, List<PatchClassReport> reports)
    {
        string? failure = retransform([.. group.Select(candidate => candidate.Type)]);

        if (failure is null)
        {
            reports.AddRange(group.Select(candidate => new PatchClassReport(candidate.Name, Failure: null)));

            return;
        }

        if (group.Length == 1)
        {
            reports.Add(new PatchClassReport(group[0].Name, failure));

            return;
        }

        int half = group.Length / 2;
        Attempt(group[..half], retransform, reports);
        Attempt(group[half..], retransform, reports);
    }
}

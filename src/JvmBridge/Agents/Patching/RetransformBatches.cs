namespace JvmBridge.Agents;

/// <summary>
/// Retransforms classes in chunks so that one class the JVM refuses does not stop the rest. A chunk the JVM rejects is split in halves until the failing class stands alone and carries the reason. Retransforming a class twice is harmless,
/// so the split is correct whether the JVM rejects a chunk atomically or only after changing some classes. The code uses plain loops, not LINQ, because every LINQ instantiation over a struct adds to every agent's native size.
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
        {
            int count = Math.Min(ChunkSize, candidates.Count - start);
            RetransformCandidate[] chunk = new RetransformCandidate[count];

            for (int index = 0; index < count; index++)
                chunk[index] = candidates[start + index];

            Attempt(chunk, retransform, reports);
        }

        return reports;
    }

    private static void Attempt(RetransformCandidate[] group, Func<nint[], string?> retransform, List<PatchClassReport> reports)
    {
        nint[] types = new nint[group.Length];

        for (int index = 0; index < group.Length; index++)
            types[index] = group[index].Type;

        string? failure = retransform(types);

        if (failure is null)
        {
            foreach (RetransformCandidate candidate in group)
                reports.Add(new PatchClassReport(candidate.Name, Failure: null));

            return;
        }

        if (group.Length == 1)
        {
            reports.Add(new PatchClassReport(group[0].Name, failure));

            return;
        }

        int half = group.Length / 2;
        RetransformCandidate[] first = new RetransformCandidate[half];
        RetransformCandidate[] second = new RetransformCandidate[group.Length - half];
        Array.Copy(group, sourceIndex: 0, first, destinationIndex: 0, half);
        Array.Copy(group, half, second, destinationIndex: 0, second.Length);
        Attempt(first, retransform, reports);
        Attempt(second, retransform, reports);
    }
}

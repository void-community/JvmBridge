namespace JvmBridge.Agents;

/// <summary>
/// The outcome of <see cref="AgentContext.PatchNow"/> or <see cref="AgentContext.Unpatch"/>: which already loaded classes were retransformed, and which could not be. A patch always applies to classes that load afterwards; the report is only about
/// classes that were loaded already.
/// </summary>
public sealed class PatchReport
{
    internal PatchReport(string? notRetransformedReason, IReadOnlyList<PatchClassReport> classes)
    {
        NotRetransformedReason = notRetransformedReason;
        Classes = classes;
    }

    /// <summary>Gets the loaded classes that the patch targets and what happened to each.</summary>
    public IReadOnlyList<PatchClassReport> Classes { get; }

    /// <summary>Gets the number of loaded classes that could not be retransformed.</summary>
    public int Failed => Classes.Count(report => !report.Succeeded);

    /// <summary>Gets why loaded classes were not looked at, for example because retransformation was not enabled, or <see langword="null"/> when they were.</summary>
    public string? NotRetransformedReason { get; }

    /// <summary>Gets the number of loaded classes that were retransformed.</summary>
    public int Retransformed => Classes.Count(report => report.Succeeded);

    /// <summary>Describes the outcome in one line suitable for a log.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        if (NotRetransformedReason is { } reason)
            return $"no loaded class was retransformed: {reason}";

        string failures = Failed == 0 ? string.Empty : $", {Failed} failed ({string.Join("; ", Classes.Where(report => !report.Succeeded).Select(report => $"{report.ClassName}: {report.Failure}"))})";

        return $"{Retransformed} loaded class{(Retransformed == 1 ? string.Empty : "es")} retransformed{failures}";
    }
}

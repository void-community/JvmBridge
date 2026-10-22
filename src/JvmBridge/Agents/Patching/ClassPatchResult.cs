namespace JvmBridge.Agents;

/// <summary>What applying a <see cref="ClassPatch"/> to a class did, or why it did nothing.</summary>
public sealed class ClassPatchResult
{
    internal ClassPatchResult(string className, byte[]? bytes, string? skippedReason, IReadOnlyList<string> details, int redirectedInvocations, int replacedConstants, int changedAccessFlags, int appliedEdits, int advisedMethods = 0, PatchSkipCategory skipCategory = PatchSkipCategory.None)
    {
        ClassName = className;
        Bytes = bytes;
        SkippedReason = skippedReason;
        Details = details;
        RedirectedInvocations = redirectedInvocations;
        ReplacedConstants = replacedConstants;
        ChangedAccessFlags = changedAccessFlags;
        AppliedEdits = appliedEdits;
        AdvisedMethods = advisedMethods;
        SkipCategory = skipCategory;
    }

    /// <summary>Gets the internal name of the patched class.</summary>
    public string ClassName { get; }

    /// <summary>Gets the patched class-file bytes, or <see langword="null"/> when the class is unchanged.</summary>
    public byte[]? Bytes { get; }

    /// <summary>Gets whether the patch changed the class.</summary>
    public bool Changed => Bytes is not null;

    /// <summary>Gets why the class was left unchanged, or <see langword="null"/> when it was changed.</summary>
    public string? SkippedReason { get; }

    /// <summary>Gets one line per applied edit, for example <c>sortTweakList()V: 1 invocation of java/util/Collections.sort redirected</c>.</summary>
    public IReadOnlyList<string> Details { get; }

    /// <summary>Gets the number of invocations that were redirected.</summary>
    public int RedirectedInvocations { get; }

    /// <summary>Gets the number of constants that were replaced.</summary>
    public int ReplacedConstants { get; }

    /// <summary>Gets the number of classes, fields and methods whose access flags changed.</summary>
    public int ChangedAccessFlags { get; }

    /// <summary>Gets the number of custom edits that ran.</summary>
    public int AppliedEdits { get; }

    /// <summary>Gets the number of methods that got a Before or After hook.</summary>
    public int AdvisedMethods { get; }

    /// <summary>Gets the kind of reason the class was left unchanged, or <see cref="PatchSkipCategory.None"/> when it was changed.</summary>
    public PatchSkipCategory SkipCategory { get; }

    /// <summary>Describes the outcome in one line suitable for a log, for example <c>patched pkg/Name (sort()V: 1 invocation of java/util/Collections.sort redirected)</c>.</summary>
    /// <returns>The description.</returns>
    public override string ToString() => Changed ? $"patched {ClassName} ({string.Join("; ", Details)})" : $"skipped {ClassName}: {SkippedReason}";
}

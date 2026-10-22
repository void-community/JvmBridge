using JvmBridge.ClassFiles;

namespace JvmBridge.Agents;

/// <summary>
/// A pure, reusable description of edits to one class. Every edit keeps constant indices and code offsets, and a patch is all or nothing: when any target is missing the class is left unchanged and the result says why.
/// </summary>
public sealed class ClassPatch
{
    private readonly PatchStep[] _steps;
    private readonly MethodScope? _scope;

    private ClassPatch(string className, PatchStep[] steps, MethodScope? scope)
    {
        ClassName = className;
        _steps = steps;
        _scope = scope;
    }

    /// <summary>Gets the internal name of the class this patch applies to.</summary>
    public string ClassName { get; }

    /// <summary>Starts a patch for a class.</summary>
    /// <param name="className">The internal class name with slashes, for example <c>cpw/mods/fml/relauncher/CoreModManager</c>.</param>
    /// <returns>An empty patch.</returns>
    public static ClassPatch For(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        if (className.Contains('.'))
            throw new ArgumentException("Use the internal class name with slashes, for example cpw/mods/fml/relauncher/CoreModManager.", nameof(className));

        return new ClassPatch(className, [], scope: null);
    }

    /// <summary>Scopes the redirects and access changes that follow to one method, until <see cref="AnyMethod"/> or another call to this method.</summary>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor, or <see langword="null"/> to match every overload.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch Method(string name, string? descriptor = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return new ClassPatch(ClassName, _steps, new MethodScope(name, descriptor));
    }

    /// <summary>Ends the scope set by <see cref="Method"/>, so following edits apply to the whole class.</summary>
    /// <returns>A new patch.</returns>
    public ClassPatch AnyMethod() => new(ClassName, _steps, scope: null);

    /// <summary>Redirects calls of a method to a static method of another class at the same length. A virtual or interface receiver becomes the first parameter.</summary>
    /// <param name="from">The call to redirect; kind, owner, name and descriptor must match.</param>
    /// <param name="toOwner">The internal name of the class that declares the replacement.</param>
    /// <param name="toName">The replacement name, or <see langword="null"/> to keep the name.</param>
    /// <param name="ownerIsInterface">Whether <paramref name="toOwner"/> is an interface, whose static methods need an interface method reference (class files of version 52 or later).</param>
    /// <returns>A new patch.</returns>
    public ClassPatch RedirectInvocation(JavaMethod from, string toOwner, string? toName = null, bool ownerIsInterface = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(toOwner);
        MethodScope? scope = _scope;

        return With(PatchKind.Invocation, editor =>
        {
            if (FindMissingMethod(editor, scope) is { } missing)
                return StepOutcome.Skip(missing);

            int count = editor.ReplaceInvocation(from, toOwner, toName, toDescriptor: null, scope?.Name, scope?.Descriptor, ownerIsInterface);

            return count == 0
                ? StepOutcome.Skip($"no invocation of {from.Owner}.{from.Name}{from.Descriptor}{(scope is null ? string.Empty : $" in {scope}")}")
                : StepOutcome.Applied(count, $"{Prefix(scope)}{count} invocation{(count == 1 ? string.Empty : "s")} of {from.Owner}.{from.Name} redirected");
        });
    }

    /// <summary>Replaces a String constant. Every use of the constant changes.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch ReplaceConstant(string oldValue, string newValue)
    {
        ArgumentNullException.ThrowIfNull(oldValue);
        ArgumentNullException.ThrowIfNull(newValue);

        return ReplacingConstant($"string \"{oldValue}\"", editor => editor.ReplaceString(oldValue, newValue));
    }

    /// <summary>Replaces an Integer constant. Values the compiler encodes inline, such as small numbers, are not constants.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch ReplaceConstant(int oldValue, int newValue) => ReplacingConstant($"int {oldValue}", editor => editor.ReplaceInteger(oldValue, newValue));

    /// <summary>Replaces a Long constant.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch ReplaceConstant(long oldValue, long newValue) => ReplacingConstant($"long {oldValue}", editor => editor.ReplaceLong(oldValue, newValue));

    /// <summary>Replaces a Float constant.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch ReplaceConstant(float oldValue, float newValue) => ReplacingConstant($"float {oldValue}", editor => editor.ReplaceFloat(oldValue, newValue));

    /// <summary>Replaces a Double constant.</summary>
    /// <param name="oldValue">The value to replace.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch ReplaceConstant(double oldValue, double newValue) => ReplacingConstant($"double {oldValue}", editor => editor.ReplaceDouble(oldValue, newValue));

    /// <summary>Replaces access flags: the scoped method's when <see cref="Method"/> is active, otherwise the class's.</summary>
    /// <param name="accessFlags">The new flags.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch SetAccess(int accessFlags)
    {
        MethodScope? scope = _scope;

        return With(PatchKind.Access, editor =>
        {
            if (scope is null)
            {
                editor.SetAccess(accessFlags);

                return StepOutcome.Applied(1, $"class access set to 0x{accessFlags:x}");
            }

            int count = editor.SetMethodAccess(scope.Name, scope.Descriptor, accessFlags);

            return count == 0 ? StepOutcome.Skip($"method {scope} not found") : StepOutcome.Applied(count, $"{scope}: access set to 0x{accessFlags:x}");
        });
    }

    /// <summary>Replaces the access flags of a field. Fields are shared by the whole class, so no method scope may be active.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="accessFlags">The new flags.</param>
    /// <param name="descriptor">The field descriptor, or <see langword="null"/> to match any.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch SetFieldAccess(string name, int accessFlags, string? descriptor = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        RequireWholeClass(nameof(SetFieldAccess));

        return With(PatchKind.Access, editor =>
        {
            int count = editor.SetFieldAccess(name, descriptor, accessFlags);

            return count == 0 ? StepOutcome.Skip($"field {name} not found") : StepOutcome.Applied(count, $"field {name}: access set to 0x{accessFlags:x}");
        });
    }

    /// <summary>Runs a custom edit with the underlying editor, for changes the other methods do not cover.</summary>
    /// <param name="edit">The edit. It must keep constant indices and code offsets valid.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch Edit(Action<ClassFileEditor> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        return With(PatchKind.Edit, editor =>
        {
            edit(editor);

            return StepOutcome.Applied(1, "custom edit applied");
        });
    }

    /// <summary>Applies the patch to class-file bytes without a JVM.</summary>
    /// <param name="classBytes">The class-file bytes.</param>
    /// <returns>The patched bytes with a count of what changed, or the reason the class was left unchanged.</returns>
    public ClassPatchResult Apply(ReadOnlySpan<byte> classBytes)
    {
        if (_steps.Length == 0)
            return Skipped("the patch has no edits");

        ClassFileEditor editor;

        try { editor = new ClassFileEditor(classBytes); }
        catch (ClassFormatException exception) { return Skipped($"not a valid class file: {exception.Message}"); }

        if (editor.Model.Name != ClassName)
            return Skipped($"the class file is {editor.Model.Name}");

        List<string> details = [];
        int[] counts = new int[4];

        foreach (PatchStep step in _steps)
        {
            StepOutcome outcome;

            try { outcome = step.Run(editor); }
            catch (Exception exception) when (exception is ClassFormatException or ArgumentException or InvalidOperationException) { return Skipped($"an edit failed: {exception.Message}"); }

            if (outcome.SkippedReason is { } reason)
                return Skipped(reason);

            counts[(int)step.Kind] += outcome.Changes;
            details.Add(outcome.Detail);
        }

        return new ClassPatchResult(ClassName, editor.ToArray(), skippedReason: null, details, counts[(int)PatchKind.Invocation], counts[(int)PatchKind.Constant], counts[(int)PatchKind.Access], counts[(int)PatchKind.Edit]);
    }

    private static string Prefix(MethodScope? scope) => scope is null ? string.Empty : $"{scope}: ";

    private static string? FindMissingMethod(ClassFileEditor editor, MethodScope? scope)
    {
        bool found = scope is null || editor.Model.Methods.Any(method => method.Name == scope.Name && (scope.Descriptor is null || method.Descriptor == scope.Descriptor));

        return found ? null : $"method {scope} not found";
    }

    private ClassPatchResult Skipped(string reason) => new(ClassName, bytes: null, reason, [], redirectedInvocations: 0, replacedConstants: 0, changedAccessFlags: 0, appliedEdits: 0);

    private ClassPatch ReplacingConstant(string description, Func<ClassFileEditor, int> replace)
    {
        RequireWholeClass("ReplaceConstant");

        return With(PatchKind.Constant, editor =>
        {
            int count = replace(editor);

            return count == 0 ? StepOutcome.Skip($"constant {description} not found") : StepOutcome.Applied(count, $"{count} constant{(count == 1 ? string.Empty : "s")} {description} replaced");
        });
    }

    private void RequireWholeClass(string operation)
    {
        if (_scope is not null)
            throw new InvalidOperationException($"{operation} applies to the whole class; call AnyMethod() after Method() first.");
    }

    private ClassPatch With(PatchKind kind, Func<ClassFileEditor, StepOutcome> run) => new(ClassName, [.. _steps, new PatchStep(kind, run)], _scope);

    private enum PatchKind
    {
        Invocation,
        Constant,
        Access,
        Edit,
    }

    private sealed record MethodScope(string Name, string? Descriptor)
    {
        public override string ToString() => Name + Descriptor;
    }

    private sealed record PatchStep(PatchKind Kind, Func<ClassFileEditor, StepOutcome> Run);

    private readonly record struct StepOutcome(int Changes, string Detail, string? SkippedReason)
    {
        internal static StepOutcome Applied(int changes, string detail) => new(changes, detail, SkippedReason: null);

        internal static StepOutcome Skip(string reason) => new(Changes: 0, string.Empty, reason);
    }
}

using JvmBridge.ClassFiles;

namespace JvmBridge.Agents;

/// <summary>
/// A pure, reusable description of edits to one class. Every edit keeps constant indices and code offsets, and a patch is all or nothing: when any target is missing the class is left unchanged and the result says why.
/// </summary>
public sealed class ClassPatch
{
    private readonly ClassMatcher _matcher;
    private readonly PatchStep[] _steps;
    private readonly MethodScope? _scope;
    private readonly AdviceStep[] _advice;

    private ClassPatch(ClassMatcher matcher, PatchStep[] steps, MethodScope? scope, AdviceStep[] advice)
    {
        _matcher = matcher;
        ClassName = matcher.ExactName ?? matcher.Description;
        _steps = steps;
        _scope = scope;
        _advice = advice;
    }

    /// <summary>Gets the internal name of the class this patch applies to, or for a patch that matches several classes a description of what it matches, such as <c>package com/acme</c>.</summary>
    public string ClassName { get; }

    internal ClassMatcher Matcher => _matcher;

    /// <summary>Starts a patch for a class.</summary>
    /// <param name="className">The internal class name with slashes, for example <c>cpw/mods/fml/relauncher/CoreModManager</c>.</param>
    /// <returns>An empty patch.</returns>
    public static ClassPatch For(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        if (className.Contains('.'))
            throw new ArgumentException("Use the internal class name with slashes, for example cpw/mods/fml/relauncher/CoreModManager.", nameof(className));

        return new ClassPatch(ClassMatcher.Exact(className), [], scope: null, advice: []);
    }

    /// <summary>Starts a patch for the classes of a package.</summary>
    /// <param name="package">The internal package name with slashes, for example <c>com/acme/service</c>.</param>
    /// <param name="includeSubpackages">Whether classes in nested packages match too.</param>
    /// <returns>An empty patch.</returns>
    public static ClassPatch ForPackage(string package, bool includeSubpackages = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(package);
        RequireInternalName(package, nameof(package));

        return new ClassPatch(ClassMatcher.Package(package.TrimEnd('/'), includeSubpackages), [], scope: null, advice: []);
    }

    /// <summary>Starts a patch for the classes whose internal name a filter accepts. The filter runs for every class that loads, so keep it cheap.</summary>
    /// <param name="nameFilter">Accepts internal class names such as <c>com/acme/Service</c>.</param>
    /// <param name="description">What the filter accepts, for logs and results.</param>
    /// <returns>An empty patch.</returns>
    public static ClassPatch ForMatching(Func<string, bool> nameFilter, string description = "classes accepted by a name filter")
    {
        ArgumentNullException.ThrowIfNull(nameFilter);
        ArgumentException.ThrowIfNullOrEmpty(description);

        return new ClassPatch(ClassMatcher.Filter(nameFilter, description), [], scope: null, advice: []);
    }

    /// <summary>
    /// Starts a patch for the classes and interfaces that extend or implement a type, directly or through other types the runtime knows. The type itself does not match. This reads the header of every class that loads;
    /// start from <see cref="ForPackage"/> and <see cref="WhereSubclassOf"/> instead when you know the package.
    /// </summary>
    /// <param name="superType">The internal name of the superclass or interface.</param>
    /// <returns>An empty patch.</returns>
    public static ClassPatch ForSubclassesOf(string superType)
    {
        ArgumentException.ThrowIfNullOrEmpty(superType);
        RequireInternalName(superType, nameof(superType));

        return new ClassPatch(ClassMatcher.Any().SubclassOf(superType), [], scope: null, advice: []);
    }

    /// <summary>
    /// Starts a patch for the classes that carry a class-level annotation, whether the annotation is visible at run time or only recorded in the class file. The annotation of a class that was already loaded cannot be read,
    /// so such a patch applies to classes that load after it is registered unless a name test narrows it, in which case the narrowed classes are retransformed and checked.
    /// </summary>
    /// <param name="annotationType">The internal name of the annotation type, for example <c>com/acme/Traced</c>.</param>
    /// <returns>An empty patch.</returns>
    public static ClassPatch ForAnnotatedWith(string annotationType)
    {
        ArgumentException.ThrowIfNullOrEmpty(annotationType);
        RequireInternalName(annotationType, nameof(annotationType));

        return new ClassPatch(ClassMatcher.Any().AnnotatedWith(annotationType), [], scope: null, advice: []);
    }

    /// <summary>Narrows the patch to classes that also carry a class-level annotation. The edits and method scope are kept.</summary>
    /// <param name="annotationType">The internal name of the annotation type.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch WhereAnnotatedWith(string annotationType)
    {
        ArgumentException.ThrowIfNullOrEmpty(annotationType);
        RequireInternalName(annotationType, nameof(annotationType));

        return new ClassPatch(_matcher.AnnotatedWith(annotationType), _steps, _scope, _advice);
    }

    /// <summary>Narrows the patch to classes that also extend or implement a type. The edits and method scope are kept.</summary>
    /// <param name="superType">The internal name of the superclass or interface.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch WhereSubclassOf(string superType)
    {
        ArgumentException.ThrowIfNullOrEmpty(superType);
        RequireInternalName(superType, nameof(superType));

        return new ClassPatch(_matcher.SubclassOf(superType), _steps, _scope, _advice);
    }

    /// <summary>Gets whether this patch targets a class, evaluating the class name, the supertypes and the class-level annotations as far as the bytes and the hierarchy tell.</summary>
    /// <param name="classBytes">The class-file bytes.</param>
    /// <param name="hierarchy">What is known about supertypes, needed only by patches that look at them; without it only direct supertypes are known.</param>
    /// <returns><see langword="true"/> when the patch targets the class.</returns>
    public bool Matches(ReadOnlySpan<byte> classBytes, ClassHierarchy? hierarchy = null)
    {
        return ClassHeader.TryRead(classBytes, includeAnnotations: _matcher.NeedsAnnotations, out ClassHeader? header) && _matcher.Matches(header, hierarchy ?? new ClassHierarchy());
    }

    /// <summary>Scopes the redirects and access changes that follow to one method, until <see cref="AnyMethod"/> or another call to this method.</summary>
    /// <param name="name">The method name.</param>
    /// <param name="descriptor">The method descriptor, or <see langword="null"/> to match every overload.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch Method(string name, string? descriptor = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return new ClassPatch(_matcher, _steps, new MethodScope(name, descriptor), _advice);
    }

    /// <summary>Ends the scope set by <see cref="Method"/>, so following edits apply to the whole class.</summary>
    /// <returns>A new patch.</returns>
    public ClassPatch AnyMethod() => new(_matcher, _steps, scope: null, _advice);

    /// <summary>Redirects calls of a method to a static method of another class at the same length. A virtual or interface receiver becomes the first parameter.</summary>
    /// <param name="from">The call to redirect; kind, owner, name and descriptor must match.</param>
    /// <param name="toOwner">The internal name of the class that declares the replacement.</param>
    /// <param name="toName">The replacement name, or <see langword="null"/> to keep the name.</param>
    /// <param name="ownerIsInterface">Whether <paramref name="toOwner"/> is an interface, whose static methods need an interface method reference (class files of version 52 or later).</param>
    /// <returns>A new patch.</returns>
    public ClassPatch RedirectInvocation(JavaMethod from, string toOwner, string? toName = null, bool ownerIsInterface = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(toOwner);

        return Redirecting(from, toOwner, toName, toDescriptor: null, ownerIsInterface);
    }

    /// <summary>
    /// Redirects calls of a method to a static method with a descriptor of its own, at the same length. The target takes the operands of the call in order, the receiver of a virtual or interface call first, and returns what the call returns. A parameter may be
    /// <c>java/lang/Object</c> where the call passes another reference, which is how a helper in the bootstrap loader receives objects of classes it cannot see: the verifier accepts any reference where <c>Object</c> is expected.
    /// </summary>
    /// <param name="from">The call to redirect; kind, owner, name and descriptor must match.</param>
    /// <param name="to">The replacement, described with <see cref="JavaMethod.Static"/>.</param>
    /// <param name="ownerIsInterface">Whether the owner of <paramref name="to"/> is an interface, whose static methods need an interface method reference (class files of version 52 or later).</param>
    /// <returns>A new patch.</returns>
    /// <exception cref="ArgumentException">The target is not static, or the operands of the call cannot be passed to it: another number of parameters, another result type, another primitive type, or a reference parameter that is neither the type of the
    /// operand nor <c>java/lang/Object</c>.</exception>
    public ClassPatch RedirectInvocation(JavaMethod from, JavaMethod to, bool ownerIsInterface = false)
    {
        if (to.Kind != JavaMethodKind.Static)
            throw new ArgumentException($"The target {to} must be static; describe it with JavaMethod.Static.", nameof(to));

        string derived = from.Kind == JavaMethodKind.Static ? from.Descriptor : ClassFileEditor.WithReceiver(from.Owner, from.Descriptor);

        if (Incompatibility(derived, to.Descriptor) is { } problem)
            throw new ArgumentException($"The call {from} cannot be redirected to {to}: {problem}.", nameof(to));

        return Redirecting(from, to.Owner, to.Name, to.Descriptor, ownerIsInterface);
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

    /// <summary>
    /// Runs a custom edit that reports what it did, so that it gets the same result, log line and all-or-nothing behavior as the built-in edits. The edit returns how many places it changed; none makes the patch skip the class with the description, and an
    /// edit that throws <see cref="ClassFormatException"/>, <see cref="ArgumentException"/> or <see cref="InvalidOperationException"/> skips it with the message. A <see cref="Method"/> scope that names a method the class lacks skips the class before the edit runs,
    /// and the scope is passed to the edit.
    /// </summary>
    /// <param name="description">What the edit changes, for the result and the log, for example <c>connection state calls moved to the event loop</c>.</param>
    /// <param name="edit">The edit. It must keep constant indices and code offsets valid.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch Edit(string description, Func<ClassEditContext, int> edit)
    {
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(edit);
        MethodScope? scope = _scope;

        return With(PatchKind.Edit, editor =>
        {
            if (FindMissingMethod(editor, scope) is { } missing)
                return StepOutcome.Skip(missing);

            int count = edit(new ClassEditContext(editor, scope?.Name, scope?.Descriptor));

            return count switch
            {
                < 0 => throw new InvalidOperationException($"The edit '{description}' returned {count}; return the number of places it changed."),
                0 => StepOutcome.Skip($"{description}: nothing to change{(scope is null ? string.Empty : $" in {scope}")}"),
                _ => StepOutcome.Applied(count, $"{Prefix(scope)}{description} ({count} change{(count == 1 ? string.Empty : "s")})"),
            };
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

    /// <summary>
    /// Calls a static method before the scoped method runs, with the receiver (<see langword="null"/> for a static method) and the arguments of the call. The hook has the descriptor <c>(Ljava/lang/Object;</c> and the parameters of the method <c>)V</c>; its first
    /// parameter may be the declaring class instead of <c>Object</c>. Scope the patch with <see cref="Method"/> and a descriptor first. The method's code moves into a private synthetic method and the method becomes a short stub, so the patch must apply when
    /// the class is first defined: it cannot advise a class that was loaded before the agent, and the log says so. Constructors, static initializers, native, abstract, bridge and interface methods are not supported. A hook that throws prevents the method from running.
    /// </summary>
    /// <param name="hook">The hook, described with <see cref="JavaMethod.Static"/>. The class that declares it must be visible to the advised class, as for <see cref="RedirectInvocation(JavaMethod, string, string?, bool)"/>.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch Before(JavaMethod hook)
    {
        MethodScope scope = RequireAdviceScope(nameof(Before));
        AdviceStep? existing = _advice.FirstOrDefault(step => step.Matches(scope));

        if (existing?.Hooks.Before is not null)
            throw new InvalidOperationException($"{scope} already has a Before hook.");

        AdviceHooks hooks = existing is null ? new AdviceHooks(hook, After: null, IncludeArguments: false) : existing.Hooks with { Before = hook };

        return WithAdvice(scope, hooks);
    }

    /// <summary>
    /// Calls a static method after the scoped method returns, with the receiver (<see langword="null"/> for a static method) and, when <paramref name="includeArguments"/> is set, the arguments of the call, then the result of a method that has one. The hook
    /// returns what the advised method returns, so it can replace the result: its descriptor is <c>(Ljava/lang/Object;</c>, the parameters if included, the result type, <c>)</c> and the result type; for a <see langword="void"/> method it ends in <c>)V</c> with no result
    /// parameter. The first parameter may be the declaring class instead of <c>Object</c>. The hook does not run when the method throws. See <see cref="Before"/> for how the method changes and what is not supported.
    /// </summary>
    /// <param name="hook">The hook, described with <see cref="JavaMethod.Static"/>.</param>
    /// <param name="includeArguments">Whether the hook receives the arguments of the call after the receiver.</param>
    /// <returns>A new patch.</returns>
    public ClassPatch After(JavaMethod hook, bool includeArguments = false)
    {
        MethodScope scope = RequireAdviceScope(nameof(After));
        AdviceStep? existing = _advice.FirstOrDefault(step => step.Matches(scope));

        if (existing?.Hooks.After is not null)
            throw new InvalidOperationException($"{scope} already has an After hook.");

        AdviceHooks hooks = existing is null ? new AdviceHooks(Before: null, hook, includeArguments) : existing.Hooks with { After = hook, IncludeArguments = includeArguments };

        return WithAdvice(scope, hooks);
    }

    /// <summary>Applies the patch to class-file bytes without a JVM.</summary>
    /// <param name="classBytes">The class-file bytes.</param>
    /// <returns>The patched bytes with a count of what changed, or the reason the class was left unchanged.</returns>
    public ClassPatchResult Apply(ReadOnlySpan<byte> classBytes)
    {
        return Run(classBytes, Phases.Edits | Phases.Advice, new AdviceContext(isRetransformation: false, recorded: null));
    }

    internal bool HasAdvice => _advice.Length > 0;

    internal bool HasEdits => _steps.Length > 0;

    /// <summary>Runs the edits of the patch, without its advice, which the engine applies to every class after all edits so that edits see the original code.</summary>
    internal ClassPatchResult ApplyEdits(ReadOnlySpan<byte> classBytes) => Run(classBytes, Phases.Edits, advice: null);

    /// <summary>Runs the advice of the patch.</summary>
    internal ClassPatchResult ApplyAdvice(ReadOnlySpan<byte> classBytes, AdviceContext advice) => Run(classBytes, Phases.Advice, advice);

    private ClassPatchResult Run(ReadOnlySpan<byte> classBytes, Phases phases, AdviceContext? advice)
    {
        if (_steps.Length == 0 && _advice.Length == 0)
            return Skipped(ClassName, "the patch has no edits");

        ClassFileEditor editor;

        try { editor = new ClassFileEditor(classBytes); }
        catch (ClassFormatException exception) { return Skipped(ClassName, $"not a valid class file: {exception.Message}"); }

        string className = editor.Model.Name;

        if (!_matcher.MatchesName(className))
            return Skipped(className, _matcher.ExactName is null ? $"the class file is {className}, which {ClassName} does not target" : $"the class file is {className}");

        List<string> details = [];
        int[] counts = new int[4];

        if (phases.HasFlag(Phases.Edits))
        {
            foreach (PatchStep step in _steps)
            {
                StepOutcome outcome;

                try { outcome = step.Run(editor); }
                catch (Exception exception) when (exception is ClassFormatException or ArgumentException or InvalidOperationException) { return Skipped(className, $"an edit failed: {exception.Message}"); }

                if (outcome.SkippedReason is { } reason)
                    return Skipped(className, reason);

                counts[(int)step.Kind] += outcome.Changes;
                details.Add(outcome.Detail);
            }
        }

        List<string> advised = [];

        if (phases.HasFlag(Phases.Advice) && advice is not null)
        {
            foreach (AdviceStep step in _advice)
            {
                string key = MethodAdvisor.Key(step.Name, step.Descriptor);

                if (advice.IsRetransformation && advice.Recorded?.Contains(key) != true)
                    return Skipped(className, $"{key}: the class is loaded already and was not advised when it was defined, so advice would add a method, which the JVM forbids", PatchSkipCategory.SchemaChangeOnRetransform);

                AdviceSkip? skip;

                try { skip = MethodAdvisor.Advise(editor, step.Name, step.Descriptor, step.Hooks); }
                catch (Exception exception) when (exception is ClassFormatException or ArgumentException or InvalidOperationException) { return Skipped(className, $"advice failed: {exception.Message}"); }

                if (skip is { } problem)
                    return Skipped(className, problem.Reason, problem.Category);

                advised.Add(key);
                details.Add($"{key}: {step.Hooks.Describe()} advice added");
            }
        }

        foreach (string key in advised)
            _ = (advice?.Applied.Add(key));

        return new ClassPatchResult(className, editor.ToArray(), skippedReason: null, details, counts[(int)PatchKind.Invocation], counts[(int)PatchKind.Constant], counts[(int)PatchKind.Access], counts[(int)PatchKind.Edit], advised.Count);
    }

    private static string? Incompatibility(string derived, string target)
    {
        ClassFiles.Advice.MethodDescriptor call = ClassFiles.Advice.MethodDescriptor.Parse(derived);
        ClassFiles.Advice.MethodDescriptor helper = ClassFiles.Advice.MethodDescriptor.Parse(target);

        if (call.Parameters.Count != helper.Parameters.Count)
            return $"the call passes {call.Parameters.Count} operand{(call.Parameters.Count == 1 ? string.Empty : "s")} ({derived}), the target takes {helper.Parameters.Count}";

        if (call.ReturnType != helper.ReturnType)
            return $"the call returns {call.ReturnType}, the target {helper.ReturnType}";

        for (int index = 0; index < call.Parameters.Count; index++)
        {
            string passed = call.Parameters[index];
            string taken = helper.Parameters[index];
            bool reference = passed[0] is 'L' or '[';

            if (passed != taken && !(reference && taken == "Ljava/lang/Object;"))
                return $"operand {index + 1} is {passed}, which the target's {taken} cannot take; use {passed} or Ljava/lang/Object;";
        }

        return null;
    }

    private ClassPatch Redirecting(JavaMethod from, string toOwner, string? toName, string? toDescriptor, bool ownerIsInterface)
    {
        MethodScope? scope = _scope;

        return With(PatchKind.Invocation, editor =>
        {
            if (FindMissingMethod(editor, scope) is { } missing)
                return StepOutcome.Skip(missing);

            int count = editor.ReplaceInvocation(from, toOwner, toName, toDescriptor, scope?.Name, scope?.Descriptor, ownerIsInterface);

            return count == 0
                ? StepOutcome.Skip($"no invocation of {from.Owner}.{from.Name}{from.Descriptor}{(scope is null ? string.Empty : $" in {scope}")}")
                : StepOutcome.Applied(count, $"{Prefix(scope)}{count} invocation{(count == 1 ? string.Empty : "s")} of {from.Owner}.{from.Name} redirected");
        });
    }

    private static string Prefix(MethodScope? scope) => scope is null ? string.Empty : $"{scope}: ";

    private static string? FindMissingMethod(ClassFileEditor editor, MethodScope? scope)
    {
        bool found = scope is null || editor.Model.Methods.Any(method => method.Name == scope.Name && (scope.Descriptor is null || method.Descriptor == scope.Descriptor));

        return found ? null : $"method {scope} not found";
    }

    private static ClassPatchResult Skipped(string className, string reason, PatchSkipCategory category = PatchSkipCategory.Other)
        => new(className, bytes: null, reason, [], redirectedInvocations: 0, replacedConstants: 0, changedAccessFlags: 0, appliedEdits: 0, advisedMethods: 0, category);

    private ClassPatch ReplacingConstant(string description, Func<ClassFileEditor, int> replace)
    {
        RequireWholeClass("ReplaceConstant");

        return With(PatchKind.Constant, editor =>
        {
            int count = replace(editor);

            return count == 0 ? StepOutcome.Skip($"constant {description} not found") : StepOutcome.Applied(count, $"{count} constant{(count == 1 ? string.Empty : "s")} {description} replaced");
        });
    }

    private static void RequireInternalName(string name, string parameter)
    {
        if (name.Contains('.'))
            throw new ArgumentException("Use the internal name with slashes, for example com/acme/Service.", parameter);
    }

    private void RequireWholeClass(string operation)
    {
        if (_scope is not null)
            throw new InvalidOperationException($"{operation} applies to the whole class; call AnyMethod() after Method() first.");
    }

    private MethodScope RequireAdviceScope(string operation)
    {
        if (_scope is not { Descriptor: not null } scope)
            throw new InvalidOperationException($"{operation} needs a method and its descriptor: call Method(name, descriptor) first.");

        return scope;
    }

    private ClassPatch WithAdvice(MethodScope scope, AdviceHooks hooks)
    {
        AdviceStep step = new(scope.Name, scope.Descriptor ?? string.Empty, hooks);
        AdviceStep[] advice = [.. _advice.Where(existing => !existing.Matches(scope)), step];

        return new ClassPatch(_matcher, _steps, _scope, advice);
    }

    private ClassPatch With(PatchKind kind, Func<ClassFileEditor, StepOutcome> run) => new(_matcher, [.. _steps, new PatchStep(kind, run)], _scope, _advice);

    private enum PatchKind
    {
        Invocation,
        Constant,
        Access,
        Edit,
    }

    [Flags]
    private enum Phases
    {
        Edits = 1,
        Advice = 2,
    }

    private sealed record AdviceStep(string Name, string Descriptor, AdviceHooks Hooks)
    {
        internal bool Matches(MethodScope scope) => Name == scope.Name && Descriptor == scope.Descriptor;
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

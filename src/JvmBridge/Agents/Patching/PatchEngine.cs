using JvmBridge.ClassFiles;
using JvmBridge.Native;
using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>
/// Matches class loads against the registered patches, applies them, defines helper classes, and retransforms classes that were already loaded. The patches live in an immutable <see cref="PatchSet"/> that the class-file hook reads
/// without locking, so a patch can be registered or removed while classes load.
/// </summary>
internal sealed class PatchEngine(AgentContext context)
{
    private readonly object _writer = new();
    private readonly ClassHierarchy _hierarchy = new();
    private readonly List<HelperState> _helpers = [];
    private readonly List<string> _bootstrapJars = [];
    private PatchSet _set = PatchSet.Empty;
    private long _sequence;

    /// <summary>Gets what is known about the supertypes of classes, for patches that look at them.</summary>
    internal ClassHierarchy Hierarchy => _hierarchy;

    /// <summary>Gets the registered patches in the order they apply.</summary>
    internal IReadOnlyList<ClassPatch> Registered => Volatile.Read(ref _set).Patches;

    /// <summary>Registers a patch for the classes that load from now on. A patch that is registered already stays as it is.</summary>
    internal void Add(ClassPatch patch)
    {
        lock (_writer)
        {
            if (!Volatile.Read(ref _set).Contains(patch))
                Volatile.Write(ref _set, _set.With(patch, ++_sequence));
        }
    }

    internal void Add(JavaClassDefinition definition, ClassLoaderTarget target)
    {
        if (_helpers.Any(helper => helper.Definition.Name == definition.Name))
            throw new InvalidOperationException($"A helper class named {definition.Name} is already registered.");

        _helpers.Add(new HelperState(definition, target));
    }

    /// <summary>Registers a patch and retransforms the loaded classes it targets.</summary>
    internal PatchReport Register(ClassPatch patch)
    {
        Add(patch);

        return RetransformTargets([patch]);
    }

    /// <summary>Removes a patch and retransforms the loaded classes it targeted, so they are transformed again without it.</summary>
    internal PatchReport Withdraw(ClassPatch patch)
    {
        bool removed;

        lock (_writer)
        {
            removed = Volatile.Read(ref _set).Contains(patch);

            if (removed)
                Volatile.Write(ref _set, _set.Without(patch));
        }

        return removed ? RetransformTargets([patch]) : new PatchReport("the patch was not registered", []);
    }

    /// <summary>Applies the patches that target a class, in registration order, and makes sure the helpers it needs exist in the defining loader, leaving the class unchanged when that fails.</summary>
    internal byte[]? Transform(ClassFile file, ClassTransformContext callback)
    {
        PatchSet set = Volatile.Read(ref _set);

        if (set.Count == 0 || file.Name is not { } name)
            return null;

        ClassHeader? header = null;

        if (set.NeedsHierarchy && ClassHeader.TryRead(file.Bytes, set.NeedsAnnotations, out header))
            _hierarchy.Record(header);

        IReadOnlyList<ClassPatch> candidates = set.Candidates(name);

        if (candidates.Count == 0)
            return null;

        byte[]? current = null;

        foreach (ClassPatch patch in candidates)
        {
            if (patch.Matcher.NeedsHeader && !MatchesHeader(patch.Matcher, file.Bytes, ref header))
                continue;

            ClassPatchResult result = patch.Apply(current is null ? file.Bytes : current);

            Report(patch, result);

            if (result.Bytes is { } changed)
                current = changed;
        }

        if (current is null)
            return null;

        string? failure = DefineLoaderHelpers(callback, current);

        if (failure is null)
            return current;

        context.Log($"skipped {name}: {failure}");

        return null;
    }

    internal void DefineBootstrapHelpers(JavaEnvironment environment)
    {
        HelperState[] pending = [.. _helpers.Where(candidate => candidate.Target == ClassLoaderTarget.Bootstrap && !candidate.IsDefinedInBootstrap)];

        if (pending.Length == 0)
            return;

        // Defining with a null loader keeps the JVM's class-data sharing for application classes, which an appended bootstrap path turns off.
        // OpenJ9's checked JNI treats a null loader as fatal, so there the helpers go in a jar on the bootstrap search path instead.
        string? failure = context.IsOpenJ9 ? DefineInBootstrapSearchPath(environment, pending) : DefineInBootstrapLoader(environment, pending);

        if (failure is not null)
            context.Log($"could not define bootstrap helpers: {failure}");
    }

    /// <summary>Deletes the temporary jars of bootstrap helpers when the agent unloads.</summary>
    internal void Cleanup()
    {
        foreach (string jar in _bootstrapJars)
            BootstrapJar.Delete(jar);

        _bootstrapJars.Clear();
    }

    /// <summary>Defines bootstrap helpers, then retransforms the loaded classes that have patches, for an agent that attached to a running JVM.</summary>
    internal void OnAttach()
    {
        PatchSet set = Volatile.Read(ref _set);

        if (_helpers.Count == 0 && set.Count == 0)
            return;

        using JavaThreadAttachment attachment = context.VirtualMachine.AttachCurrentThread();

        DefineBootstrapHelpers(attachment.Environment);

        if (set.Count > 0)
            _ = RetransformTargets(set.Patches);
    }

    /// <summary>Defines bootstrap helpers and, when a patch looks at supertypes, learns the supertypes of the classes that loaded before the hierarchy was recorded.</summary>
    internal void OnVmInit(JavaEnvironment environment)
    {
        DefineBootstrapHelpers(environment);

        if (Volatile.Read(ref _set).NeedsHierarchy)
            SeedHierarchy(environment);
    }

    private static string? ToInternalName(string signature)
    {
        return signature.Length > 2 && signature[0] == 'L' && signature[^1] == ';' ? signature[1..^1] : null;
    }

    private bool MatchesHeader(ClassMatcher matcher, ReadOnlySpan<byte> bytes, ref ClassHeader? header)
    {
        if (header is null && !ClassHeader.TryRead(bytes, includeAnnotations: true, out header))
            return false;

        return matcher.Matches(header, _hierarchy);
    }

    private void Report(ClassPatch patch, ClassPatchResult result)
    {
        // A patch that names its class always logs, so a typo shows up. A patch that matches many classes only logs what it changed, because most of the classes it sees lack the edit's target.
        if (patch.Matcher.ExactName is not null || result.Changed)
            context.Log(result.ToString());
        else
            context.Log(AgentLogLevel.Debug, result.ToString());
    }

    private PatchReport RetransformTargets(IReadOnlyList<ClassPatch> patches)
    {
        if (!context.CanRetransform)
            return Unretransformed("retransformation was not enabled; call TryEnableRetransformation from Configure so that loaded classes can be retransformed");

        if (context.GetPhase() != jvmtiPhase.JVMTI_PHASE_LIVE)
            return Unretransformed("the JVM is not live yet, so the patches apply to the classes that load from now on");

        List<ClassPatch> selectable = [.. patches.Where(patch => !patch.Matcher.IsUnfilteredByName || patch.Matcher.NeedsHierarchy)];

        if (selectable.Count == 0)
            return Unretransformed("a patch with no name test and no supertype test cannot tell which loaded classes it targets, so it applies to the classes that load from now on; start from ForPackage or add WhereSubclassOf");

        using JavaThreadAttachment attachment = context.VirtualMachine.AttachCurrentThread();
        JavaEnvironment environment = attachment.Environment;

        if (selectable.Any(patch => patch.Matcher.NeedsHierarchy))
            SeedHierarchy(environment);

        nint[] loaded = context.GetLoadedClasses(environment);
        List<RetransformCandidate> candidates = [];
        List<PatchClassReport> reports = [];

        try
        {
            foreach (nint type in loaded)
            {
                string? name = NameOf(type);

                if (name is null || !selectable.Any(patch => Targets(patch.Matcher, name)))
                {
                    environment.DeleteLocal(type);

                    continue;
                }

                if (context.IsModifiableClass(type))
                    candidates.Add(new RetransformCandidate(name, type));
                else
                {
                    reports.Add(new PatchClassReport(name, "the class is not modifiable"));
                    environment.DeleteLocal(type);
                }
            }

            reports.AddRange(RetransformBatches.Run(candidates, context.TryRetransform));
        }
        finally
        {
            foreach (RetransformCandidate candidate in candidates)
                environment.DeleteLocal(candidate.Type);
        }

        PatchReport report = new(notRetransformedReason: null, reports);

        foreach (PatchClassReport failed in reports.Where(entry => !entry.Succeeded))
            context.Log(AgentLogLevel.Warning, $"could not retransform {failed.ClassName}: {failed.Failure}");

        context.Log(AgentLogLevel.Debug, report.ToString());

        return report;

        bool Targets(ClassMatcher matcher, string className)
        {
            return matcher.MatchesName(className) && (!matcher.NeedsHierarchy || matcher.MatchesHierarchy(className, _hierarchy));
        }

        PatchReport Unretransformed(string reason)
        {
            context.Log(AgentLogLevel.Debug, reason);

            return new PatchReport(reason, []);
        }
    }

    private string? NameOf(nint type)
    {
        try
        {
            return ToInternalName(context.GetClassSignature(type));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void SeedHierarchy(JavaEnvironment environment)
    {
        nint[] loaded = context.GetLoadedClasses(environment);

        try
        {
            foreach (nint type in loaded)
            {
                string? name = NameOf(type);

                if (name is null || _hierarchy.Contains(name))
                    continue;

                try
                {
                    _hierarchy.Add(name, SuperclassName(environment, type), InterfaceNames(environment, type));
                }
                catch (Exception exception) when (exception is InvalidOperationException or JavaException or JniException)
                {
                    // A class that cannot be inspected stays unknown, which only means that patches cannot match through it.
                    context.Log(AgentLogLevel.Trace, $"could not read the supertypes of {name}: {exception.Message}");
                }
            }
        }
        finally
        {
            foreach (nint type in loaded)
                environment.DeleteLocal(type);
        }
    }

    private string? SuperclassName(JavaEnvironment environment, nint type)
    {
        using JavaLocalReference parent = environment.GetSuperclass(type);

        return parent.Handle == 0 ? null : ToInternalName(context.GetClassSignature(parent.Handle));
    }

    private List<string> InterfaceNames(JavaEnvironment environment, nint type)
    {
        List<string> names = [];

        foreach (nint contract in context.GetImplementedInterfaces(environment, type))
        {
            try
            {
                if (ToInternalName(context.GetClassSignature(contract)) is { } name)
                    names.Add(name);
            }
            finally
            {
                environment.DeleteLocal(contract);
            }
        }

        return names;
    }

    private static string? DefineInBootstrapLoader(JavaEnvironment environment, HelperState[] pending)
    {
        foreach (HelperState helper in pending)
        {
            try
            {
                foreach (JavaClassDefinition definition in helper.Definition.GetDefinitionOrder())
                {
                    using JavaLocalReference type = environment.DefineClass(definition.Name, loader: 0, definition.Bytes.Span);

                    definition.RegisterNatives?.Invoke(environment, type.Handle);
                }
            }
            catch (Exception exception) when (exception is JavaException or JniException)
            {
                return $"{helper.Definition.Name}: {Describe(exception)}";
            }

            helper.MarkDefinedInBootstrap();
        }

        return null;
    }

    private string? DefineInBootstrapSearchPath(JavaEnvironment environment, HelperState[] pending)
    {
        List<JavaClassDefinition> classes = [.. pending.SelectMany(helper => helper.Definition.GetDefinitionOrder())];
        string? jar = null;

        try
        {
            jar = BootstrapJar.Create(classes);
            context.AddToBootstrapSearch(jar);
            _bootstrapJars.Add(jar);

            // Loading each class now registers its native methods before any code can call it.
            foreach (JavaClassDefinition definition in classes)
            {
                using JavaLocalReference type = environment.FindClass(definition.Name);

                definition.RegisterNatives?.Invoke(environment, type.Handle);
            }

            foreach (HelperState helper in pending)
                helper.MarkDefinedInBootstrap();

            return null;
        }
        catch (Exception exception) when (exception is JavaException or JniException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (jar is not null && !_bootstrapJars.Contains(jar))
                BootstrapJar.Delete(jar);

            return Describe(exception);
        }
    }

    private string? DefineLoaderHelpers(ClassTransformContext callback, byte[] patched)
    {
        // A helper is defined for the classes that mention it, so a class of java.base can be patched by an agent that also has loader helpers for application classes.
        HelperState[] needed = [.. _helpers.Where(helper => helper.Target == ClassLoaderTarget.PatchedClassLoader && Mentions(patched, helper.Definition.Name))];

        if (needed.Length == 0)
            return null;

        if (!callback.CanUseJni || callback.Environment is not { } environment)
            return $"helper {needed[0].Definition.Name} cannot be defined before JNI is available";

        if (callback.Loader == 0)
            return $"helper {needed[0].Definition.Name} cannot be defined for a class of the bootstrap loader; declare it with ClassLoaderTarget.Bootstrap";

        foreach (HelperState helper in needed)
        {
            string? failure = DefineIn(helper, environment, callback.Loader);

            if (failure is not null)
                return $"helper {helper.Definition.Name} could not be defined: {failure}";
        }

        return null;
    }

    private static bool Mentions(byte[] classBytes, string name)
    {
        return classBytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(name)) >= 0;
    }

    /// <summary>Defines a helper and its nested classes in a loader unless it already is, returning the reason when that fails.</summary>
    private static string? DefineIn(HelperState helper, JavaEnvironment environment, nint loader)
    {
        lock (helper.Sync)
        {
            if (helper.IsDefinedIn(environment, loader))
                return null;


            try
            {
                foreach (JavaClassDefinition definition in helper.Definition.GetDefinitionOrder())
                {
                    using JavaLocalReference type = environment.DefineClass(definition.Name, loader, definition.Bytes.Span);

                    definition.RegisterNatives?.Invoke(environment, type.Handle);
                }
            }
            catch (Exception exception) when (exception is JavaException or JniException)
            {
                return Describe(exception);
            }

            helper.Remember(environment, loader);

            return null;
        }
    }

    private static string Describe(Exception exception) => exception is JavaException { JavaTypeName: { } type } java ? $"{type}: {java.JavaMessage}" : exception.Message;

    private sealed class HelperState(JavaClassDefinition definition, ClassLoaderTarget target)
    {
        private readonly List<JavaWeakGlobalReference> _loaders = [];

        internal JavaClassDefinition Definition { get; } = definition;

        internal ClassLoaderTarget Target { get; } = target;

        internal object Sync { get; } = new();

        internal bool IsDefinedInBootstrap { get; private set; }

        internal void MarkDefinedInBootstrap() => IsDefinedInBootstrap = true;

        internal bool IsDefinedIn(JavaEnvironment environment, nint loader)
        {
            for (int index = _loaders.Count - 1; index >= 0; index--)
            {
                using JavaLocalReference? known = _loaders[index].TryPromote(environment);

                if (known is null)
                {
                    _loaders[index].Dispose();
                    _loaders.RemoveAt(index);

                    continue;
                }

                if (environment.IsSameObject(known.Handle, loader))
                    return true;
            }

            return false;
        }

        internal void Remember(JavaEnvironment environment, nint loader) => _loaders.Add(environment.NewWeakGlobalReference(loader));
    }
}

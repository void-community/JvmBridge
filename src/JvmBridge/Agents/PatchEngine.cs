using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>Filters class loads by name, applies registered patches, defines helper classes, and retransforms classes that were already loaded when the agent attached.</summary>
internal sealed class PatchEngine(AgentContext context)
{
    private readonly Dictionary<string, List<ClassPatch>> _patches = new(StringComparer.Ordinal);
    private readonly List<HelperState> _helpers = [];
    private readonly List<string> _bootstrapJars = [];

    internal void Add(ClassPatch patch)
    {
        if (!_patches.TryGetValue(patch.ClassName, out List<ClassPatch>? existing))
            _patches.Add(patch.ClassName, existing = []);

        existing.Add(patch);
    }

    internal void Add(JavaClassDefinition definition, ClassLoaderTarget target)
    {
        if (_helpers.Any(helper => helper.Definition.Name == definition.Name))
            throw new InvalidOperationException($"A helper class named {definition.Name} is already registered.");

        _helpers.Add(new HelperState(definition, target));
    }

    /// <summary>Applies the patches registered for a class and makes sure its helpers exist in the defining loader, leaving the class unchanged when that fails.</summary>
    internal byte[]? Transform(ClassFile file, ClassTransformContext callback)
    {
        if (file.Name is not { } name || !_patches.TryGetValue(name, out List<ClassPatch>? patches))
            return null;

        byte[]? current = null;

        foreach (ClassPatch patch in patches)
        {
            ClassPatchResult result = patch.Apply(current is null ? file.Bytes : current);

            context.Log(result.ToString());

            if (result.Bytes is { } changed)
                current = changed;
        }

        if (current is null)
            return null;

        string? failure = DefineLoaderHelpers(callback);

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
        }
        catch (Exception exception) when (exception is JavaException or JniException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            context.Log($"could not define bootstrap helpers: {Describe(exception)}");

            if (jar is not null && !_bootstrapJars.Contains(jar))
                BootstrapJar.Delete(jar);
        }
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
        if (_helpers.Count == 0 && _patches.Count == 0)
            return;

        using JavaThreadAttachment attachment = context.VirtualMachine.AttachCurrentThread();

        DefineBootstrapHelpers(attachment.Environment);
        RetransformLoadedClasses(attachment.Environment);
    }

    private void RetransformLoadedClasses(JavaEnvironment environment)
    {
        if (_patches.Count == 0)
            return;

        if (!context.CanRetransform)
        {
            context.Log("retransformation is unavailable, so classes that are already loaded stay unpatched");

            return;
        }

        nint[] classes = context.GetLoadedClasses(environment);

        try
        {
            foreach (nint type in classes)
            {
                string signature = context.GetClassSignature(type);

                if (signature.Length > 2 && signature[0] == 'L' && signature[^1] == ';' && _patches.ContainsKey(signature[1..^1]) && context.IsModifiableClass(type))
                    context.Retransform(type);
            }
        }
        finally
        {
            foreach (nint type in classes)
                environment.DeleteLocal(type);
        }
    }

    private string? DefineLoaderHelpers(ClassTransformContext callback)
    {
        HelperState[] needed = [.. _helpers.Where(helper => helper.Target == ClassLoaderTarget.PatchedClassLoader)];

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

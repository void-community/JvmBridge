using System.Collections.Concurrent;

using JvmBridge.Native;
using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>
/// The packages of named modules that an agent exports or opens to the modules its helpers are defined in. A grant is made when its helper is defined: in the boot loader's unnamed module with the bootstrap helpers, and in a class loader's unnamed
/// module when a helper is defined there, before any patched class can call it, so the helper's first link already sees the package. A JVM before Java 9 has no modules and needs none.
/// </summary>
internal sealed unsafe class ModuleAccess(AgentContext context)
{
    private readonly List<Grant> _grants = [];
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);

    /// <summary>Records a grant for a helper; recording the same one again does nothing.</summary>
    internal void Add(string module, string package, string helper, bool open)
    {
        Grant grant = new(module, package, helper, open);

        if (!_grants.Contains(grant))
            _grants.Add(grant);
    }

    /// <summary>Makes the grants of a helper in the module that a class of the helper was defined in, logging each one that cannot be made once.</summary>
    /// <param name="environment">The JNI environment of the thread that defined the class.</param>
    /// <param name="helper">The name of the helper, as it was declared.</param>
    /// <param name="type">A class of the helper.</param>
    internal void GrantTo(JavaEnvironment environment, string helper, nint type)
    {
        Grant[] grants = [.. _grants.Where(grant => grant.Helper == helper)];

        if (grants.Length == 0 || context.JavaMajor < 9)
            return;

        foreach (Grant grant in grants)
        {
            string? failure;

            try { failure = Make(environment, grant, type); }
            catch (Exception exception) when (exception is JavaException or JniException or MissingMethodException) { failure = exception is JavaException { JavaTypeName: { } name } java ? $"{name}: {java.JavaMessage}" : exception.Message; }

            if (failure is null)
                context.Log(AgentLogLevel.Debug, $"{grant.Verb}ed {grant.Module}/{grant.Package} to the module of {helper}");
            else if (_reported.TryAdd(grant.ToString(), value: 0))
                context.Log(AgentLogLevel.Warning, $"could not {grant.Verb} {grant.Module}/{grant.Package} to {helper}: {failure}");
        }
    }

    // The modules of the boot layer hold the JDK's; ModuleLayer.boot() is null until the JVM has created it.
    private static JavaLocalReference FindModule(JavaEnvironment environment, string name)
    {
        using JavaLocalReference layerType = environment.FindClass(name: "java/lang/ModuleLayer");
        using JavaLocalReference optionalType = environment.FindClass(name: "java/util/Optional");
        nint boot = environment.GetMethod(layerType.Handle, name: "boot", signature: "()Ljava/lang/ModuleLayer;", isStatic: true);
        nint find = environment.GetMethod(layerType.Handle, name: "findModule", signature: "(Ljava/lang/String;)Ljava/util/Optional;");
        nint orElse = environment.GetMethod(optionalType.Handle, name: "orElse", signature: "(Ljava/lang/Object;)Ljava/lang/Object;");
        JavaLocalReference layer = environment.CallObject(layerType.Handle, boot, [], isStatic: true);

        if (layer.Handle == 0)
            return layer;

        using JavaLocalReference owned = layer;
        using JavaLocalReference text = environment.NewString(name);
        using JavaLocalReference found = environment.CallObject(layer.Handle, find, [JavaValue.Object(text.Handle)]);

        return environment.CallObject(found.Handle, orElse, [JavaValue.Object(value: 0)]);
    }

    private string? Make(JavaEnvironment environment, Grant grant, nint type)
    {
        using JavaLocalReference source = FindModule(environment, grant.Module);

        if (source.Handle == 0)
            return $"the boot layer has no module {grant.Module}";

        using JavaLocalReference classType = environment.GetObjectClass(type);
        nint getModule = environment.GetMethod(classType.Handle, name: "getModule", signature: "()Ljava/lang/Module;");
        using JavaLocalReference target = environment.CallObject(type, getModule, []);

        // OpenJ9 before 0.54 calls the void Module.implAddExportsOrOpens with CallObjectMethod in these two functions, which its checked JNI aborts on and which otherwise turns whatever the call leaves behind into a local reference. There
        // the grant goes through jdk.internal.module.Modules instead, as HotSpot's own AddModuleExports and AddModuleOpens do.
        if (context.IsOpenJ9)
            return CallModules(environment, grant, source.Handle, target.Handle);

        jvmtiInterface_1_** tooling = (jvmtiInterface_1_**)context.RawHandle;
        jvmtiError result;

        // The function takes the package name as Module.addExports does, with dots.
        fixed (byte* package = ModifiedUtf8.Encode(grant.Package.Replace(oldChar: '/', newChar: '.')))
        {
            result = grant.Open
                ? (*tooling)->AddModuleOpens(tooling, (_jobject*)source.Handle, package, (_jobject*)target.Handle)
                : (*tooling)->AddModuleExports(tooling, (_jobject*)source.Handle, package, (_jobject*)target.Handle);
        }

        return result switch
        {
            jvmtiError.JVMTI_ERROR_NONE => null,
            jvmtiError.JVMTI_ERROR_ILLEGAL_ARGUMENT => $"module {grant.Module} has no package {grant.Package}",
            jvmtiError.JVMTI_ERROR_UNMODIFIABLE_MODULE => $"module {grant.Module} cannot be changed",
            jvmtiError.JVMTI_ERROR_WRONG_PHASE => "the JVM is not live yet",
            _ => $"JVMTI error {result}",
        };
    }

    // JNI ignores module boundaries, so the class of an unexported package can be called; it throws IllegalArgumentException for a package the module does not have.
    private static string? CallModules(JavaEnvironment environment, Grant grant, nint source, nint target)
    {
        using JavaLocalReference modules = environment.FindClass(name: "jdk/internal/module/Modules");
        nint method = environment.GetMethod(modules.Handle, grant.Open ? "addOpens" : "addExports", signature: "(Ljava/lang/Module;Ljava/lang/String;Ljava/lang/Module;)V", isStatic: true);
        using JavaLocalReference package = environment.NewString(grant.Package.Replace(oldChar: '/', newChar: '.'));

        try
        {
            environment.CallVoid(modules.Handle, method, [JavaValue.Object(source), JavaValue.Object(package.Handle), JavaValue.Object(target)], isStatic: true);
        }
        catch (JavaException exception) when (exception.JavaTypeName == "java.lang.IllegalArgumentException")
        {
            return $"module {grant.Module} has no package {grant.Package}";
        }

        return null;
    }

    private sealed record Grant(string Module, string Package, string Helper, bool Open)
    {
        internal string Verb => Open ? "open" : "export";
    }
}

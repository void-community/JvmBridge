using JvmBridge.Agents;
using JvmBridge.ClassFiles;

namespace MyJvmAgent;

/// <summary>The edits this agent makes, kept apart from the agent so tests can apply them to class bytes without a JVM.</summary>
public static class Patches
{
    /// <summary>
    /// A placeholder patch: when <c>example/Target</c> loads, its <c>sorted</c> method calls the Java helper in <c>java/redirects/Sorting.java</c> instead of <c>Collections.sort</c>.
    /// Replace the class, method and call with what your agent changes.
    /// </summary>
    public static readonly ClassPatch SortDescending = ClassPatch.For("example/Target")
        .Method("sorted", "(Ljava/util/List;)V")
        .RedirectInvocation(JavaMethod.Static("java/util/Collections", "sort", "(Ljava/util/List;Ljava/util/Comparator;)V"), toOwner: "redirects/Sorting");
}

/// <summary>Registers the helper class and the patch. JvmBridge generates the native entry points for this class.</summary>
public sealed class PatcherAgent : JavaAgent
{
    /// <inheritdoc/>
    public override void Configure(AgentContext context)
    {
        context.DefineClass(JavaClasses.Redirects.Sorting, ClassLoaderTarget.Bootstrap);
        context.Patch(Patches.SortDescending);
    }
}

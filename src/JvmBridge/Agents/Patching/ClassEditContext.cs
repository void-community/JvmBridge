using JvmBridge.ClassFiles;

namespace JvmBridge.Agents;

/// <summary>What a reporting <see cref="ClassPatch.Edit(string, Func{ClassEditContext, int})"/> works with: the editor, and the method that <see cref="ClassPatch.Method"/> scoped the patch to.</summary>
public sealed class ClassEditContext
{
    internal ClassEditContext(ClassFileEditor editor, string? methodName, string? methodDescriptor)
    {
        Editor = editor;
        MethodName = methodName;
        MethodDescriptor = methodDescriptor;
    }

    /// <summary>Gets the editor of the class.</summary>
    public ClassFileEditor Editor { get; }

    /// <summary>Gets the descriptor of the scoped method, or <see langword="null"/> when the scope names every overload or there is no scope.</summary>
    public string? MethodDescriptor { get; }

    /// <summary>Gets the name of the scoped method, or <see langword="null"/> when the patch is not scoped to a method.</summary>
    public string? MethodName { get; }
}

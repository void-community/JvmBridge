namespace JvmBridge.ClassFiles.Code;

/// <summary>A place in code that branches jump to. <see cref="CodeBuilder.NewLabel"/> makes one, <see cref="CodeBuilder.Mark"/> puts it at the next instruction, and a branch to it can come before or after that.</summary>
public sealed class CodeLabel
{
    internal CodeLabel(CodeBuilder owner)
    {
        Owner = owner;
    }

    internal int? Depth { get; set; }

    internal int Offset { get; set; } = -1;

    internal CodeBuilder Owner { get; }
}

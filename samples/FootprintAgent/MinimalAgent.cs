using JvmBridge.Agents;
using JvmBridge.ClassFiles;

namespace FootprintAgent;

/// <summary>A minimal agent for measuring footprint: it defines one helper class and registers one patch for a class that the measured launches never load, so the numbers are the cost of having an agent loaded.</summary>
public sealed class MinimalAgent : JavaAgent
{
    private const string Helper = "jvmbridge/footprint/Helpers";

    private const string Target = "jvmbridge/footprint/Target";

    /// <inheritdoc/>
    public override void Configure(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.DefineClass(CreateHelper(), ClassLoaderTarget.Bootstrap);
        context.Patch(ClassPatch.For(Target).ReplaceConstant(oldValue: "footprint-before", newValue: "footprint-after"));
    }

    // static int one() { return 1; }
    private static JavaClassDefinition CreateHelper()
    {
        byte[] code = [0x04, 0xac];

        return JavaClassDefinition.FromBytes(new ClassFileWriter(Helper).AddMethod(name: "one", descriptor: "()I", maxStack: 1, maxLocals: 0, code).ToArray());
    }
}

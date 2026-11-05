using JvmBridge.Agents;
using JvmBridge.ClassFiles;
using JvmBridge.ClassFiles.Code;

using Xunit;

namespace JvmBridge.Tests.ClassFiles.Patching;

/// <summary>Verifies <see cref="ClassPatch.ReplaceConstant(long, long)"/> and its overloads after <see cref="ClassPatch.Method"/>, which change only the scoped method's loads.</summary>
public sealed class ScopedConstantTests
{
    private const string TitleDescriptor = "(Ljava/lang/String;)V";

    private const string Window = "test/Window";

    private static readonly byte[] Subject = new ClassFileWriter(Window)
        .AddMethod(name: "initWindow", TitleDescriptor, Loads)
        .AddMethod(name: "initWindow", descriptor: "()V", Loads)
        .AddMethod(name: "render", descriptor: "()V", Loads)
        .ToArray();

    private static ClassPatch Scoped => ClassPatch.For(Window).Method(name: "initWindow", TitleDescriptor);

    /// <summary>A scope without a descriptor covers every overload, and each load counts once.</summary>
    [Fact]
    public void AScopeWithoutADescriptorCoversEveryOverload()
    {
        ClassPatchResult result = ClassPatch.For(Window).Method(name: "initWindow").ReplaceConstant(oldValue: 10L, newValue: 30L).Apply(Subject);
        JavaClassFile patched = JavaClassFile.Read(result.Bytes ?? throw new InvalidOperationException(result.SkippedReason));

        Assert.Equal(expected: 2, result.ReplacedConstants);
        Assert.Equal(["30", "100000", "ten"], LoadedValues(patched, descriptor: "()V"));
        Assert.Equal(["10", "100000", "ten"], LoadedValues(patched, descriptor: "()V", name: "render"));
    }

    /// <summary>An <c>ldc</c> reaches only the first 256 constants, so a new String that would be constant 300 skips the class instead of changing the load's length.</summary>
    [Fact]
    public void AnLdcThatCannotReachTheNewValueSkipsTheClass()
    {
        ClassFileWriter writer = new ClassFileWriter(Window).AddMethod(name: "initWindow", TitleDescriptor, Loads);

        for (int value = 0; value < 300; value++)
            Assert.True(writer.AddInteger(1_000_000 + value) > 0);

        ClassPatchResult result = Scoped.ReplaceConstant(oldValue: "ten", newValue: "thirty").Apply(writer.ToArray());

        Assert.False(result.Changed);
        Assert.Contains(expectedSubstring: "ldc", result.SkippedReason, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "first 256 constants", result.SkippedReason, StringComparison.Ordinal);
    }

    /// <summary>After <see cref="ClassPatch.AnyMethod"/> the constant changes for the whole class, as without a scope.</summary>
    [Fact]
    public void AnyMethodReplacesTheConstantInTheWholeClass()
    {
        ClassPatchResult result = Scoped.AnyMethod().ReplaceConstant(oldValue: 10L, newValue: 30L).Apply(Subject);
        JavaClassFile patched = JavaClassFile.Read(result.Bytes ?? throw new InvalidOperationException(result.SkippedReason));

        Assert.Equal(expected: 1, result.ReplacedConstants);
        Assert.Equal(["30", "100000", "ten"], LoadedValues(patched, descriptor: "()V", name: "render"));
    }

    /// <summary>A scope that loads no such constant, or names no method, skips the class with the reason.</summary>
    [Fact]
    public void NothingToReplaceSkipsTheClass()
    {
        ClassPatchResult absent = Scoped.ReplaceConstant(oldValue: 99L, newValue: 30L).Apply(Subject);
        ClassPatchResult missing = ClassPatch.For(Window).Method(name: "absent").ReplaceConstant(oldValue: 10L, newValue: 30L).Apply(Subject);

        Assert.Equal(expected: "no load of constant long 99 in initWindow(Ljava/lang/String;)V", absent.SkippedReason);
        Assert.Equal(expected: "method absent not found", missing.SkippedReason);
    }

    /// <summary>The scoped method loads the new long, int and String, the other methods still load the old ones, and the old entries stay in the constant pool for them.</summary>
    [Fact]
    public void OnlyTheScopedMethodLoadsTheNewValues()
    {
        ClassPatchResult result = Scoped.ReplaceConstant(oldValue: 10L, newValue: 30L).ReplaceConstant(oldValue: 100_000, newValue: 300_000).ReplaceConstant(oldValue: "ten", newValue: "thirty").Apply(Subject);
        JavaClassFile patched = JavaClassFile.Read(result.Bytes ?? throw new InvalidOperationException(result.SkippedReason));

        Assert.Equal(expected: 3, result.ReplacedConstants);
        Assert.Equal(["30", "300000", "thirty"], LoadedValues(patched, TitleDescriptor));
        Assert.Equal(["10", "100000", "ten"], LoadedValues(patched, descriptor: "()V"));
        Assert.Equal(["10", "100000", "ten"], LoadedValues(patched, descriptor: "()V", name: "render"));
        Assert.Contains(expected: "initWindow(Ljava/lang/String;)V: 1 load of constant long 10 replaced", result.Details);
    }

    private static string[] LoadedValues(JavaClassFile file, string descriptor, string name = "initWindow")
    {
        CodeAttribute code = file.FindMethod(name, descriptor)?.Code ?? throw new InvalidOperationException($"{name}{descriptor} has no code.");

        return [.. code.Instructions
            .Where(static instruction => instruction.Opcode is Opcodes.Ldc or Opcodes.LdcW or Opcodes.Ldc2W)
            .Select(
                instruction => file.ConstantPool[instruction.ConstantIndex ?? 0] switch
            {
                LongEntry number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                IntegerEntry number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringEntry text => file.GetUtf8(text.StringIndex),
                _ => "other",
            }
            )];
    }

    // Loads 10L, 100000 and "ten" with ldc2_w and ldc, and pops them.
    private static CodeBuilder Loads(CodeBuilder code)
    {
        return code.LConst(value: 10).Op(Opcodes.Pop2).IConst(value: 100_000).Pop().Ldc(value: "ten").Pop().Return();
    }
}

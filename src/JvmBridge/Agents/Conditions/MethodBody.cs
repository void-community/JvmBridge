using JvmBridge.ClassFiles;

namespace JvmBridge.Agents;

/// <summary>
/// The code of the methods a <see cref="ClassPatch.When"/> condition looks at: the method that <see cref="ClassPatch.Method"/> scoped the patch to, every overload of it when the scope names no descriptor, or every method of the class without a scope. A condition
/// combines its tests with <c>&amp;&amp;</c>, <c>||</c> and <c>!</c>.
/// </summary>
public sealed class MethodBody
{
    private readonly JavaClassFile _file;
    private readonly IReadOnlyList<Instruction> _instructions;

    internal MethodBody(JavaClassFile file, IReadOnlyList<Instruction> instructions)
    {
        _file = file;
        _instructions = instructions;
    }

    /// <summary>Tells whether the code accesses a field with <c>getfield</c>, <c>putfield</c>, <c>getstatic</c> or <c>putstatic</c>.</summary>
    /// <param name="owner">The internal name of the class the instruction names as the field's owner, for example <c>com/acme/Service</c>.</param>
    /// <param name="name">The name the field is declared with.</param>
    /// <returns><see langword="true"/> when an instruction accesses that field.</returns>
    public bool AccessesField(string owner, string name) => _instructions.Any(instruction => _file.AccessesField(instruction, owner, name));

    /// <summary>Tells whether the code calls a method with the invoke instruction of its kind.</summary>
    /// <param name="method">The call to look for.</param>
    /// <returns><see langword="true"/> when an instruction is that call.</returns>
    public bool Calls(JavaMethod method) => _instructions.Any(instruction => _file.Invokes(instruction, method));

    /// <summary>Tells whether the code constructs an object of a class with <c>new</c>.</summary>
    /// <param name="className">The internal name of the class, for example <c>java/util/HashMap</c>.</param>
    /// <returns><see langword="true"/> when a <c>new</c> instruction names that class.</returns>
    public bool Creates(string className) => _instructions.Any(instruction => instruction.Opcode == Opcodes.New && _file.ReferencesClass(instruction, className));
}

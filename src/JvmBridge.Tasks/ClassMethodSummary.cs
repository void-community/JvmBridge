namespace JvmBridge.Tasks;

/// <summary>A method of a class file: its access flags, name and descriptor.</summary>
/// <param name="AccessFlags">The <c>ACC_</c> bits, which tell public from private and static from instance methods.</param>
/// <param name="Name">The name Java code calls it by, or <c>&lt;init&gt;</c> for a constructor.</param>
/// <param name="Descriptor">The parameter and return types in JVM notation, for example <c>(I)Ljava/lang/String;</c>, which tell overloads apart.</param>
internal sealed record ClassMethodSummary(int AccessFlags, string Name, string Descriptor);

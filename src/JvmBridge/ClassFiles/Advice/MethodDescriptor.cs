namespace JvmBridge.ClassFiles.Advice;

/// <summary>A parsed method descriptor: the type descriptor of each parameter and of the result, and how many local variable slots they take.</summary>
internal sealed class MethodDescriptor
{
    private MethodDescriptor(IReadOnlyList<string> parameters, string returnType)
    {
        Parameters = parameters;
        ReturnType = returnType;
        ParameterSlots = parameters.Sum(Slots);
        ReturnSlots = returnType == "V" ? 0 : Slots(returnType);
    }

    /// <summary>Gets the descriptor of each parameter, for example <c>I</c>, <c>[B</c> or <c>Ljava/lang/String;</c>.</summary>
    internal IReadOnlyList<string> Parameters { get; }

    /// <summary>Gets the descriptor of the result, <c>V</c> for none.</summary>
    internal string ReturnType { get; }

    /// <summary>Gets the number of local variable slots the parameters take, two for each long and double.</summary>
    internal int ParameterSlots { get; }

    /// <summary>Gets the number of stack slots the result takes: none, one, or two for a long and a double.</summary>
    internal int ReturnSlots { get; }

    /// <summary>Gets the parameter descriptors as they appear between the parentheses.</summary>
    internal string ParameterText => string.Concat(Parameters);

    /// <summary>Parses a method descriptor.</summary>
    /// <param name="descriptor">The descriptor, for example <c>(ILjava/lang/String;)V</c>.</param>
    /// <returns>The parsed descriptor.</returns>
    internal static MethodDescriptor Parse(string descriptor)
    {
        if (descriptor.Length < 3 || descriptor[0] != '(')
            throw new ClassFormatException($"'{descriptor}' is not a method descriptor.");

        List<string> parameters = [];
        int index = 1;

        while (index < descriptor.Length && descriptor[index] != ')')
        {
            int end = EndOfType(descriptor, index);

            parameters.Add(descriptor[index..end]);
            index = end;
        }

        if (index >= descriptor.Length - 1)
            throw new ClassFormatException($"'{descriptor}' is not a method descriptor.");

        string returnType = descriptor[(index + 1)..];

        if (returnType != "V" && EndOfType(returnType, 0) != returnType.Length)
            throw new ClassFormatException($"'{descriptor}' is not a method descriptor.");

        return new MethodDescriptor(parameters, returnType);
    }

    /// <summary>Gets the number of slots a type takes: two for <c>J</c> and <c>D</c>, one for every other type.</summary>
    /// <param name="type">The type descriptor.</param>
    /// <returns>The slot count.</returns>
    internal static int Slots(string type) => type is "J" or "D" ? 2 : 1;

    private static int EndOfType(string text, int start)
    {
        int index = start;

        while (index < text.Length && text[index] == '[')
            index++;

        if (index >= text.Length)
            throw new ClassFormatException($"'{text}' is not a method descriptor.");

        if (text[index] == 'L')
        {
            int semicolon = text.IndexOf(value: ';', index);

            return semicolon < 0 ? throw new ClassFormatException($"'{text}' is not a method descriptor.") : semicolon + 1;
        }

        return "ZBCSIJFD".Contains(text[index], StringComparison.Ordinal) ? index + 1 : throw new ClassFormatException($"'{text}' is not a method descriptor.");
    }
}

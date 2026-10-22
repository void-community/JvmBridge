using JvmBridge.ClassFiles;
using JvmBridge.Runtime;

namespace JvmBridge.Agents;

/// <summary>A helper class to define in the JVM: its class-file bytes, any nested classes it needs, and an optional step that registers native methods once it is defined.</summary>
public sealed class JavaClassDefinition
{
    private readonly byte[] _bytes;

    /// <summary>Initializes a definition.</summary>
    /// <param name="name">The internal class name, for example <c>pkg/Helper</c>.</param>
    /// <param name="bytes">The class-file bytes; the class they describe must be named <paramref name="name"/>.</param>
    /// <param name="nestedClasses">Classes that must be defined together with this one, such as inner and anonymous classes.</param>
    /// <param name="registerNatives">Called with the JNI environment and the defined class reference to register native methods.</param>
    /// <exception cref="ArgumentException">The bytes are not a class file, or describe a class other than <paramref name="name"/>.</exception>
    public JavaClassDefinition(string name, ReadOnlySpan<byte> bytes, IReadOnlyList<JavaClassDefinition>? nestedClasses = null, Action<JavaEnvironment, nint>? registerNatives = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        JavaClassFile file;

        try { file = JavaClassFile.Read(bytes); }
        catch (ClassFormatException exception) { throw new ArgumentException($"The bytes for {name} are not a valid class file: {exception.Message}", nameof(bytes), exception); }

        if (file.Name != name)
            throw new ArgumentException($"The bytes describe {file.Name}, not {name}.", nameof(bytes));

        Name = name;
        _bytes = bytes.ToArray();
        NestedClasses = nestedClasses ?? [];
        RegisterNatives = registerNatives;
        SuperName = file.SuperName;
        Interfaces = file.Interfaces;
    }

    /// <summary>Gets the internal class name.</summary>
    public string Name { get; }

    /// <summary>Gets the class-file bytes.</summary>
    public ReadOnlyMemory<byte> Bytes => _bytes;

    /// <summary>Gets the classes defined together with this one.</summary>
    public IReadOnlyList<JavaClassDefinition> NestedClasses { get; }

    internal Action<JavaEnvironment, nint>? RegisterNatives { get; }

    private string? SuperName { get; }

    private IReadOnlyList<string> Interfaces { get; }

    /// <summary>Creates a definition from class-file bytes, taking the name from the class file.</summary>
    /// <param name="bytes">The class-file bytes.</param>
    /// <returns>The definition.</returns>
    public static JavaClassDefinition FromBytes(ReadOnlySpan<byte> bytes) => new(JavaClassFile.Read(bytes).Name, bytes);

    /// <summary>Flattens this class and its nested classes in an order that defines every superclass and interface of the group before its subclasses.</summary>
    internal List<JavaClassDefinition> GetDefinitionOrder()
    {
        List<JavaClassDefinition> remaining = [];
        Collect(this, remaining);
        HashSet<string> names = [.. remaining.Select(definition => definition.Name)];
        HashSet<string> defined = [];
        List<JavaClassDefinition> ordered = [];

        while (remaining.Count > 0)
        {
            int ready = remaining.FindIndex(definition => DependenciesOf(definition).Where(names.Contains).All(defined.Contains));

            if (ready < 0)
                throw new InvalidOperationException($"The classes of {Name} depend on each other in a cycle.");

            JavaClassDefinition next = remaining[ready];
            remaining.RemoveAt(ready);
            _ = defined.Add(next.Name);
            ordered.Add(next);
        }

        return ordered;
    }

    private static IEnumerable<string> DependenciesOf(JavaClassDefinition definition) => (definition.SuperName is null ? [] : new[] { definition.SuperName }).Concat(definition.Interfaces);

    private static void Collect(JavaClassDefinition definition, List<JavaClassDefinition> into)
    {
        into.Add(definition);

        foreach (JavaClassDefinition nested in definition.NestedClasses)
            Collect(nested, into);
    }
}

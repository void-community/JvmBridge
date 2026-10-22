using System.Reflection;

using JvmBridge.ClassFiles;

namespace JvmBridge.Tests;

internal static class ClassFileFixtures
{
    /// <summary>Gets the javac-compiled <c>fixture/Sample</c> class (Java 8, no debug information).</summary>
    internal static byte[] Sample => Read(resource: "ClassFiles.Sample.class");

    internal static int CountOf<TEntry>(this JavaClassFile file, Func<TEntry, bool> match)
        where TEntry : ConstantPoolEntry
    {
        return file.ConstantPool.OfType<TEntry>().Count(match);
    }

    internal static CodeAttribute GetCode(this JavaClassFile file, string name)
    {
        return file.GetMethod(name).Code ?? throw new InvalidOperationException($"Method {name} has no code.");
    }

    internal static ClassMember GetField(this JavaClassFile file, string name)
    {
        return file.Fields.FirstOrDefault(field => field.Name == name) ?? throw new InvalidOperationException($"The class has no field {name}.");
    }

    internal static ClassMember GetMethod(this JavaClassFile file, string name)
    {
        return file.FindMethod(name) ?? throw new InvalidOperationException($"The class has no method {name}.");
    }

    internal static string[] GetStrings(this JavaClassFile file)
    {
        return [.. file.ConstantPool.OfType<StringEntry>().Select(entry => file.GetUtf8(entry.StringIndex))];
    }

    internal static byte[] Read(string resource)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"Missing embedded fixture {resource}.");

        using MemoryStream copy = new();

        stream.CopyTo(copy);

        return copy.ToArray();
    }
}

using System.Reflection;

using Microsoft.CodeAnalysis;

namespace JvmBridge.Tests;

/// <summary>The assemblies of the .NET runtime that the tests compile sources against.</summary>
internal static class RuntimeReferences
{
    /// <summary>Creates a reference to every assembly in the runtime's directory. On Windows the directory also holds native libraries with the same extension, which a compiler can't read.</summary>
    /// <returns>The references, to which a test adds its own.</returns>
    internal static List<MetadataReference> Create()
    {
        string directory = Path.GetDirectoryName(typeof(string).Assembly.Location)
            ?? throw new InvalidOperationException(message: "Runtime assembly directory unavailable.");

        return [.. Directory.EnumerateFiles(directory, searchPattern: "*.dll")
            .Where(IsAssembly)
            .Select(static path => MetadataReference.CreateFromFile(path))];
    }

    private static bool IsAssembly(string path)
    {
        try
        {
            return AssemblyName.GetAssemblyName(path).Name is not null;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }
}

namespace JvmBridge.Tool.Tests;

/// <summary>Locates the files the tests read and gives them a scratch folder.</summary>
internal static class ToolFixtures
{
    /// <summary>Gets the directory of the checked-in class files.</summary>
    internal static string ClassFiles => Path.Combine(AppContext.BaseDirectory, path2: "Fixtures", path3: "ClassFiles");

    /// <summary>Gets the path of <c>fixture/Sample.class</c>.</summary>
    internal static string Sample => Path.Combine(ClassFiles, path2: "fixture", path3: "Sample.class");

    /// <summary>Creates an empty folder under the temporary directory.</summary>
    /// <returns>The folder, which the test deletes.</returns>
    internal static string CreateFolder()
    {
        return Directory.CreateTempSubdirectory(prefix: "jvmbridge-tool-").FullName;
    }
}

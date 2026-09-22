using System.Text;

namespace JvmBridge.Build.Infrastructure;

internal static class TextFile
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static string NormalizeLineEndings(string value)
    {
        return value.Replace(oldValue: "\r\n", newValue: "\n", StringComparison.Ordinal).Replace(oldChar: '\r', newChar: '\n');
    }

    public static void Write(string path, string value)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            FileSystem.EnsureDirectory(directory);

        File.WriteAllText(path, NormalizeLineEndings(value), Utf8);
    }
}

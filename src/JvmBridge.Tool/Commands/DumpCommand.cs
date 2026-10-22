using System.CommandLine;
using System.IO.Compression;

using JvmBridge.ClassFiles;

namespace JvmBridge.Tool.Commands;

/// <summary>Disassembles a class from a class file or a jar, in the style of <c>javap -c</c>.</summary>
internal static class DumpCommand
{
    private const string ClassExtension = ".class";

    internal static Command Create()
    {
        Argument<string> path = new(name: "path") { Description = "A .class file, or a .jar that holds the class named by --class." };
        Option<string?> className = new(name: "--class") { Description = "The internal name of the class to dump from a jar, for example com/acme/Service. Without it the classes of the jar are listed." };
        Option<string?> method = new(name: "--method") { Description = "Dump only the methods with this name." };
        Command command = new(name: "dump", description: "Disassemble a class in the style of javap -c.");

        command.Arguments.Add(path);
        command.Options.Add(className);
        command.Options.Add(method);
        command.SetAction(
            result => Run(result.GetValue(path) ?? string.Empty, result.GetValue(className), result.GetValue(method), Console.Out, Console.Error)
        );

        return command;
    }

    internal static int Run(string path, string? className, string? method, TextWriter output, TextWriter error)
    {
        if (!File.Exists(path))
        {
            error.WriteLine($"jvmbridge: {path} does not exist.");

            return 1;
        }

        if (!ClassInputs.IsArchive(path))
        {
            output.Write(ClassFileDisassembler.Disassemble(File.ReadAllBytes(path), method));

            return 0;
        }

        using ZipArchive archive = ZipFile.OpenRead(path);

        if (className is null)
        {
            foreach (string name in archive.Entries.Where(static entry => entry.FullName.EndsWith(ClassExtension, StringComparison.Ordinal)).Select(static entry => entry.FullName[..^ClassExtension.Length]).Order(StringComparer.Ordinal))
                output.WriteLine(name);

            return 0;
        }

        ZipArchiveEntry? found = archive.GetEntry(className + ClassExtension);

        if (found is null)
        {
            error.WriteLine($"jvmbridge: {path} has no class {className}. Run 'jvmbridge dump {path}' to list its classes.");

            return 1;
        }

        using MemoryStream bytes = new();

        using (Stream stream = found.Open())
            stream.CopyTo(bytes);

        output.Write(ClassFileDisassembler.Disassemble(bytes.ToArray(), method));

        return 0;
    }
}

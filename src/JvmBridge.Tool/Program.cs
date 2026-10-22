using System.CommandLine;

using JvmBridge.Tool.Commands;

namespace JvmBridge.Tool;

internal static class Program
{
    public static async Task<int> Main(string[] arguments)
    {
        RootCommand root = new(description: "Inspect class files, preview agent patches, and pack native agents for java -javaagent.");

        root.Subcommands.Add(DumpCommand.Create());
        root.Subcommands.Add(PatchCommand.Create());
        root.Subcommands.Add(PackCommand.Create());

        return await root.Parse(arguments).InvokeAsync().ConfigureAwait(continueOnCapturedContext: false);
    }
}

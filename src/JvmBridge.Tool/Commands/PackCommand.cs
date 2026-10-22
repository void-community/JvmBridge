using System.CommandLine;

using JvmBridge.Launcher;

namespace JvmBridge.Tool.Commands;

/// <summary>Packs published native agent libraries into a jar that <c>java -javaagent:agent.jar=options</c> starts.</summary>
internal static class PackCommand
{
    internal static Command Create()
    {
        Option<string[]> agents = new(name: "--agent") { Description = "A native agent library for one platform as <rid>=<path>, for example linux-x64=bin/linux-x64/MyAgent.so. Repeat it for every platform.", Required = true, AllowMultipleArgumentsPerToken = true };
        Option<string> output = new(name: "--output", aliases: ["-o"]) { Description = "The jar to write.", Required = true };
        Command command = new(name: "pack", description: "Pack native agent libraries into a jar for java -javaagent.");

        command.Options.Add(agents);
        command.Options.Add(output);
        command.SetAction(result => Run(result.GetValue(agents) ?? [], result.GetValue(output) ?? string.Empty, Console.Out, Console.Error));

        return command;
    }

    internal static int Run(IReadOnlyList<string> agents, string output, TextWriter writer, TextWriter error)
    {
        List<AgentLibrary> libraries = [];

        foreach (string agent in agents)
        {
            int separator = agent.IndexOf(value: '=', StringComparison.Ordinal);
            string rid = separator > 0 ? agent[..separator] : string.Empty;
            string path = separator > 0 ? agent[(separator + 1)..] : string.Empty;

            if (!LauncherJar.KnownRids.Contains(rid))
            {
                error.WriteLine(
                    $"jvmbridge: '{agent}' is not <rid>=<path> with a known runtime identifier. Known: {string.Join(separator: ", ", LauncherJar.KnownRids)}."
                );

                return 1;
            }

            if (!File.Exists(path))
            {
                error.WriteLine($"jvmbridge: the agent library for {rid} does not exist: {path}");

                return 1;
            }

            if (libraries.Any(library => library.Rid == rid))
            {
                error.WriteLine($"jvmbridge: {rid} is given more than once.");

                return 1;
            }

            libraries.Add(new AgentLibrary(rid, path));
        }

        if (libraries.Count == 0)
        {
            error.WriteLine(value: "jvmbridge: pass at least one --agent <rid>=<path>.");

            return 1;
        }

        string launcher = LauncherJar.Write(output, libraries);
        string platforms = string.Join(separator: ", ", libraries.Select(library => library.Rid).Order(StringComparer.Ordinal));

        writer.WriteLine($"Wrote {output} (launcher {launcher}) with {platforms}. Start it with: java -javaagent:{output}=<options> -jar app.jar");

        return 0;
    }
}

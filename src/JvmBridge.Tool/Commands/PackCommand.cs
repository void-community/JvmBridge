using System.CommandLine;

using JvmBridge.Launcher;

namespace JvmBridge.Tool.Commands;

/// <summary>Packs published native agent libraries, and the libraries of other agent jars, into a jar that <c>java -javaagent:agent.jar=options</c> starts.</summary>
internal static class PackCommand
{
    internal static Command Create()
    {
        Option<string[]> agents = new(name: "--agent") { Description = "A native agent library for one platform as <rid>=<path>, for example linux-x64=bin/linux-x64/MyAgent.so. Repeat it for every platform.", AllowMultipleArgumentsPerToken = true };
        Option<string[]> jars = new(name: "--jar") { Description = "A jar that jvmbridge pack or a publish with JvmBridgeAgentJar wrote, whose libraries go into the new jar. Repeat it to combine the jars of several platforms.", AllowMultipleArgumentsPerToken = true };
        Option<string> output = new(name: "--output", aliases: ["-o"]) { Description = "The jar to write.", Required = true };
        Command command = new(name: "pack", description: "Pack native agent libraries, or the jars of single platforms, into one jar for java -javaagent.");

        command.Options.Add(agents);
        command.Options.Add(jars);
        command.Options.Add(output);
        command.SetAction(
            result => Run(result.GetValue(agents) ?? [], result.GetValue(jars) ?? [], result.GetValue(output) ?? string.Empty, Console.Out, Console.Error)
        );

        return command;
    }

    internal static int Run(IReadOnlyList<string> agents, IReadOnlyList<string> jars, string output, TextWriter writer, TextWriter error)
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

            libraries.Add(AgentLibrary.FromFile(rid, path));
        }

        foreach (string jar in jars)
        {
            if (!File.Exists(jar))
            {
                error.WriteLine($"jvmbridge: the jar does not exist: {jar}");

                return 1;
            }

            try
            {
                libraries.AddRange(LauncherJar.Read(jar));
            }
            catch (InvalidDataException failure)
            {
                error.WriteLine($"jvmbridge: {failure.Message}");

                return 1;
            }
        }

        if (libraries.Count == 0)
        {
            error.WriteLine(value: "jvmbridge: pass at least one --agent <rid>=<path> or --jar <path>.");

            return 1;
        }

        IReadOnlyList<AgentLibrary> combined;

        try
        {
            combined = LauncherJar.Combine(libraries);
        }
        catch (InvalidDataException failure)
        {
            error.WriteLine($"jvmbridge: {failure.Message}");

            return 1;
        }

        string launcher = LauncherJar.Write(output, combined);
        string platforms = string.Join(separator: ", ", combined.Select(static library => library.Rid).Order(StringComparer.Ordinal));

        writer.WriteLine($"Wrote {output} (launcher {launcher}) with {platforms}. Start it with: java -javaagent:{output}=<options> -jar app.jar");

        return 0;
    }
}

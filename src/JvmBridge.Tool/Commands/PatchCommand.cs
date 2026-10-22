using System.CommandLine;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;

using JvmBridge.Agents;
using JvmBridge.ClassFiles;
using JvmBridge.Tool.Offline;

namespace JvmBridge.Tool.Commands;

/// <summary>Previews what an agent's patches would do to class files, without a JVM: runs the agent's <c>Configure</c> against an offline JVM and applies the patches it registered to the classes given.</summary>
internal static class PatchCommand
{
    internal static Command Create()
    {
        Option<bool> dryRun = new(name: "--dry-run") { Description = "Only report what the patches would change. Required: the command does not write class files." };
        Option<string> agent = new(name: "--agent") { Description = "The managed assembly of the agent: the .dll that dotnet build writes, before it is compiled to native code.", Required = true };
        Option<string[]> classes = new(name: "--classes") { Description = "The classes to patch: .class files, directories of them, or .jar files.", Required = true, AllowMultipleArgumentsPerToken = true };
        Option<string> options = new(name: "--options") { Description = "The agent options, as passed after the = of -agentpath or -javaagent.", DefaultValueFactory = static result => string.Empty };
        Option<bool> disassemble = new(name: "--disassemble") { Description = "Also print the disassembly of every patched class." };
        Command command = new(name: "patch", description: "Preview what an agent's patches would change in class files (needs --dry-run).");

        command.Options.Add(dryRun);
        command.Options.Add(agent);
        command.Options.Add(classes);
        command.Options.Add(options);
        command.Options.Add(disassemble);
        command.SetAction(
            result =>
            {
                if (!result.GetValue(dryRun))
                {
                    Console.Error.WriteLine(value: "jvmbridge: 'patch' only previews for now; add --dry-run.");

                    return 1;
                }

                return Run(
                    Load(result.GetValue(agent) ?? string.Empty),
                    result.GetValue(classes) ?? [],
                    result.GetValue(options) ?? string.Empty,
                    result.GetValue(disassemble),
                    Console.Out,
                    Console.Error
                );
            }
        );

        return command;
    }

    internal static int Run(Assembly? assembly, IReadOnlyList<string> classPaths, string options, bool disassemble, TextWriter output, TextWriter error)
    {
        if (assembly is null)
        {
            error.WriteLine(value: "jvmbridge: the agent assembly could not be loaded.");

            return 1;
        }

        Type[] agents = [.. FindAgents(assembly, error)];

        if (agents.Length != 1)
        {
            error.WriteLine($"jvmbridge: {assembly.GetName().Name} must contain exactly one concrete JavaAgent, found {agents.Length}.");

            return 1;
        }

        return RunAgent(CreateAgent(agents[0]), classPaths, options, disassemble, output, error);
    }

    internal static int RunAgent(JavaAgent agent, IReadOnlyList<string> classPaths, string options, bool disassemble, TextWriter output, TextWriter error)
    {
        List<ClassInput> classes = [];

        foreach (string path in classPaths)
        {
            if (!ClassInputs.TryRead(path, classes, error))
                return 1;
        }

        using OfflineJvm jvm = new();

        AgentContext context = new(agent, jvm.Machine, options, attached: false, agent.GetType().Assembly.GetName().Name);

        int status = Configure(context, output);
        IReadOnlyList<ClassPatch> patches = context.RegisteredPatches;

        output.WriteLine($"{patches.Count} patch(es) registered by Configure; {classes.Count} class(es) examined.");

        if (patches.Count == 0)
        {
            output.WriteLine(value: "Patches that an agent registers later, in OnLoad or at runtime, are not visible to a dry run.");

            return status;
        }

        Preview(patches, classes, disassemble, output);

        return status;
    }

    private static int Configure(AgentContext context, TextWriter output)
    {
        try
        {
            context.Agent.Configure(context);

            return 0;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            output.WriteLine($"Configure threw {failure.GetType().Name}: {failure.Message}. The patches registered before it threw are listed.");

            return 1;
        }
    }

    private static JavaAgent CreateAgent(Type type)
    {
        ConstructorInfo constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes)
            ?? throw new InvalidOperationException($"{type.FullName} needs a parameterless constructor.");

        return Expression.Lambda<Func<JavaAgent>>(Expression.New(constructor)).Compile()();
    }

    private static IEnumerable<Type> FindAgents(Assembly assembly, TextWriter error)
    {
        Type[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException failure)
        {
            foreach (Exception loader in failure.LoaderExceptions.OfType<Exception>().DistinctBy(static candidate => candidate.Message))
                error.WriteLine($"jvmbridge: {loader.Message}");

            types = [.. failure.Types.OfType<Type>()];
        }

        return types.Where(static type => type is { IsAbstract: false, IsClass: true } && type.IsSubclassOf(typeof(JavaAgent)));
    }

    private static Assembly? Load(string path)
    {
        string full = Path.GetFullPath(path);

        if (!File.Exists(full))
        {
            Console.Error.WriteLine($"jvmbridge: {full} does not exist.");

            return null;
        }

        string directory = Path.GetDirectoryName(full) ?? ".";

        // The agent's own dependencies sit beside it; JvmBridge itself is the copy this tool carries, whatever version the agent was built with.
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name == typeof(AgentContext).Assembly.GetName().Name)
                return typeof(AgentContext).Assembly;

            string candidate = Path.Combine(directory, name.Name + ".dll");

            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(full);
    }

    private static void Preview(IReadOnlyList<ClassPatch> patches, List<ClassInput> classes, bool disassemble, TextWriter output)
    {
        ClassHierarchy hierarchy = new();

        foreach (ClassInput input in classes)
        {
            if (ClassHeader.TryRead(input.Bytes, includeAnnotations: false, out ClassHeader? header))
                hierarchy.Add(header.Name, header.SuperName, header.Interfaces);
        }

        int changed = 0;

        foreach (ClassInput input in classes.OrderBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            byte[] current = input.Bytes;
            bool patched = false;

            foreach (ClassPatch patch in patches.Where(candidate => candidate.Matches(input.Bytes, hierarchy)))
            {
                ClassPatchResult result = patch.Apply(current);

                output.WriteLine($"{input.Name} <- {patch.ClassName}: {result}");

                if (result.Bytes is { } bytes)
                {
                    current = bytes;
                    patched = true;
                }
            }

            if (!patched)
                continue;

            changed++;

            if (disassemble)
                output.Write(ClassFileDisassembler.Disassemble(current));
        }

        output.WriteLine($"{changed} class(es) would change.");
    }
}

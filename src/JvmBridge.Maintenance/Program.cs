using System.CommandLine;

using JvmBridge.Build.Infrastructure;

namespace JvmBridge.Maintenance;

internal static class Program
{
    public static async Task<int> Main(string[] arguments)
    {
        using HttpService http = new();

        RepositoryContext repository = RepositoryContext.Discover();
        JdkResolver resolver = new(repository, http);
        JdkUpdater updater = new(repository, http, resolver, new JdkArchiveManager(http));
        RootCommand root = new(description: "Maintain pinned JDK inventories and upstream headers.");
        Command resolve = new(name: "resolve", description: "Resolve vendor archives without changing headers.");
        resolve.SetAction((result, cancellationToken) => resolver.ResolveAsync(cancellationToken));
        Command update = new(name: "update", description: "Refresh JDK inventories and header provenance; then run the Generate MSBuild target.");
        update.SetAction((result, cancellationToken) => updater.UpdateAsync(cancellationToken));
        Argument<string> measurements = new(name: "directory") { Description = "A directory that contains the hotspot-*.json files of the footprint-* CI artifacts." };
        Command footprint = new(name: "footprint", description: "Rewrite docs/footprint.md and docs/footprint.baseline.json from CI footprint measurements.");
        footprint.Arguments.Add(measurements);
        footprint.SetAction(
            result => new FootprintReporter(repository).Update(result.GetValue(measurements) ?? throw new InvalidOperationException(message: "A measurements directory is required."))
        );
        root.Subcommands.Add(resolve);
        root.Subcommands.Add(footprint);
        root.Subcommands.Add(update);

        return await root.Parse(arguments).InvokeAsync().ConfigureAwait(continueOnCapturedContext: false);
    }
}

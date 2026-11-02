using System.Text.Json.Nodes;

using JvmBridge.Build.Infrastructure;
using JvmBridge.Build.Models;

namespace JvmBridge.Maintenance;

internal sealed class JdkUpdater(RepositoryContext repository, HttpService http, JdkResolver resolver, JdkArchiveManager archives)
{
    private static readonly string[] HeaderNames = ["jni.h", "jvmti.h", "classfile_constants.h", "linux/jni_md.h"];
    private readonly RepositoryContext _repository = repository;
    private readonly HttpService _http = http;
    private readonly JdkResolver _resolver = resolver;
    private readonly JdkArchiveManager _archives = archives;

    public async Task UpdateAsync(CancellationToken cancellationToken)
    {
        JsonObject available = (await _http.GetJsonAsync(url: "https://api.adoptium.net/v3/info/available_releases", cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).RequireObject(description: "available Java releases");
        int latest = available.Property(propertyName: "most_recent_feature_release").RequireInt32(description: "most recent Java feature release");

        await _resolver.ResolveAsync(latest, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        TargetConfiguration target = Compatibility.Configuration.Targets.First(static value => value.Rid == "linux-arm64");
        JdkArchive archive = await _resolver.ResolveTemurinAsync(target, latest, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) ?? throw new InvalidOperationException(message: "Latest stable header source is unavailable");

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-headers-");

        string home = await _archives.UnpackAsync(archive, temporary.Path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        string headers = _repository.PathFromRoot(parts: ["src", "JvmBridge.Build", "headers"]);

        foreach (string name in HeaderNames)
        {
            string destination = Path.Combine(headers, name.Replace(oldChar: '/', Path.DirectorySeparatorChar));
            string? parent = Path.GetDirectoryName(destination);

            if (!string.IsNullOrEmpty(parent))
                FileSystem.EnsureDirectory(parent);

            File.Copy(Path.Combine(home, path2: "include", name.Replace(oldChar: '/', Path.DirectorySeparatorChar)), destination, overwrite: true);
        }

        ConsoleOutput.WriteLine($"Copied the OpenJDK headers of {archive.Distribution} {archive.Version} from {archive.Url}.");
    }
}

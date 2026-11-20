using System.Security.Cryptography;

using JvmBridge.Build.Infrastructure;

namespace JvmBridge.IntegrationTests;

/// <summary>
/// The glibc 2.12 sysroot that CI's tests link samples/LegacyAgent against, so that the agent loads on CentOS 6, where the pinned Java 6 and 7 run. No image .NET publishes is that old, so the sysroot is unpacked
/// from the last CentOS 6 packages, pinned by checksum: glibc and its headers, the kernel headers, and gcc's start files and libgcc. Their absolute links are made relative, so they resolve inside the sysroot.
/// glibc 2.12 lacks aligned_alloc (2.16), which the .NET runtime calls, so the sysroot's libc_nonshared.a, which every link against this glibc pulls in, gets a hidden copy built on posix_memalign.
/// </summary>
internal static class LegacySysroot
{
    private const string AlignedAllocation = """
        #include <stdlib.h>

        __attribute__((visibility("hidden"))) void *aligned_alloc(size_t alignment, size_t size)
        {
            void *memory = 0;

            return posix_memalign(&memory, alignment, size) == 0 ? memory : 0;
        }
        """;

    private const string Source = "https://vault.centos.org/6.10/os/x86_64/Packages/";

    private static readonly (string Sha256, string Name)[] Packages =
    [
        ("b893707c0dc87a556274feb53c544515a1d3b0fe2743c454dece682f5b7d5719", "glibc-2.12-1.212.el6.x86_64"),
        ("65b97f1f868afc090b1beec846490d1e6974751f188fd42b1ebc59d1c2866549", "glibc-devel-2.12-1.212.el6.x86_64"),
        ("7f1f3a2cf562465f2c18031e4e76734987ea8489c1202301e019dfb722ff5031", "glibc-headers-2.12-1.212.el6.x86_64"),
        ("93b20d547637e3d25a9604b8034334afa910f61898da0f265a3969a68bf8cc6d", "kernel-headers-2.6.32-754.el6.x86_64"),
        ("e21940ae5910d19e930217e6cc13a91c664c342c27efc045d76ca02792689cfa", "gcc-4.4.7-23.el6.x86_64"),
        ("5e32d14ebe08101d07aaedacd5a04c8bd911d15b1a368e54fcb8241a1f5aeecb", "libgcc-4.4.7-23.el6.x86_64"),
    ];

    /// <summary>Unpacks the sysroot into artifacts/legacy-sysroot, once, with rpm2cpio, cpio, clang and ar.</summary>
    /// <param name="repository">The repository whose artifacts hold the sysroot.</param>
    /// <param name="processes">Runs the tools.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The sysroot's directory.</returns>
    public static async Task<string> PrepareAsync(RepositoryContext repository, ProcessRunner processes, CancellationToken cancellationToken)
    {
        string sysroot = Path.Combine(repository.Artifacts, path2: "legacy-sysroot");
        string complete = Path.Combine(sysroot, path2: ".complete");

        if (File.Exists(complete))
            return sysroot;

        using TemporaryDirectory work = new(prefix: "jvmbridge-legacy-sysroot-");

        using HttpService http = new();

        FileSystem.EnsureDirectory(sysroot);

        foreach ((string sha256, string name) in Packages)
        {
            string package = Path.Combine(work.Path, name + ".rpm");

            await http.DownloadFileAsync(Source + name + ".rpm", package, cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            string actual;
            FileStream stream = File.OpenRead(package);

            await using (stream.ConfigureAwait(continueOnCapturedContext: false))
            {
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false));
            }

            if (!string.Equals(actual, sha256, StringComparison.Ordinal))
                throw new InvalidOperationException("SHA256 mismatch: " + name);

            await processes
                .RunCheckedAsync(command: ["sh", "-c", "rpm2cpio \"$1\" | cpio -idmu --quiet", "sh", package],
                    workingDirectory: sysroot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);
        }

        List<string> links = [];

        MakeLinksRelative(sysroot, sysroot, links);

        await File.WriteAllLinesAsync(Path.Combine(sysroot, path2: ".links"), links, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        string source = Path.Combine(work.Path, path2: "aligned_alloc.c");
        string compiled = Path.Combine(work.Path, path2: "aligned_alloc.o");

        await File.WriteAllTextAsync(source, AlignedAllocation, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        await processes.RunCheckedAsync(command: ["clang", "--target=x86_64-linux-gnu", "--sysroot=" + sysroot, "-fPIC", "-O2", "-c", source, "-o", compiled], cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        await processes.RunCheckedAsync(command: ["ar", "rs", Path.Combine(sysroot, path2: "usr", path3: "lib64", path4: "libc_nonshared.a"), compiled], cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        await File.WriteAllTextAsync(complete, string.Empty, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return sysroot;
    }

    // A link is never followed, so a link to a directory is rewritten like any other and the walk stays inside the sysroot. The rewritten links are listed for the record.
    private static void MakeLinksRelative(string root, string directory, List<string> links)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (new FileInfo(path).LinkTarget is { } target)
            {
                if (!target.StartsWith(value: '/'))
                    continue;

                File.Delete(path);

                FileSystemInfo link = File.CreateSymbolicLink(path, Path.GetRelativePath(directory, root + target));

                links.Add(link.FullName + " -> " + link.LinkTarget);
            }
            else if (Directory.Exists(path))
            {
                MakeLinksRelative(root, path, links);
            }
        }
    }
}

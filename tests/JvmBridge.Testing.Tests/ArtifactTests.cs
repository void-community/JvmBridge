using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;
using Xunit.Sdk;

namespace JvmBridge.Testing.Tests;

/// <summary>Verifies checksum-pinned Maven artifacts, patching across a jar, and the link check of helpers against real classes and the JDK.</summary>
public sealed class ArtifactTests
{
    private const string Inject = "javax.inject:javax.inject:1";

    private const string InjectAddress = "https://repo1.maven.org/maven2/javax/inject/javax.inject/1/javax.inject-1.jar";

    private const string InjectSha256 = "91c77044a50c481636c32d916fd89c9118a72195390452c81065080f957de7ff";

    private const string MavenCentral = "https://repo1.maven.org/maven2";

    private const string SourcesAddress = "https://repo1.maven.org/maven2/javax/inject/javax.inject/1/javax.inject-1-sources.jar";

    private const string SourcesSha256 = "c4b87ee2911c139c3daf498a781967f1eb2e75bc1a8529a2e7b328a15d0e433e";

    private static readonly JavaMethod Get = JavaMethod.Interface(owner: "javax/inject/Provider", name: "get", descriptor: "()Ljava/lang/Object;");

    /// <summary>A file whose checksum is not the pinned one fails the test and is not kept.</summary>
    [Fact]
    public async Task AnotherChecksumFailsAsync()
    {
        MavenArtifact wrong = new(MavenCentral, Inject, new string(c: '0', count: 64));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => wrong.GetPathAsync(TestContext.Current.CancellationToken));

        Assert.Contains(expectedSubstring: "not the pinned", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Two files cached under the same name do not collide, because each sits in a directory named after its checksum, and a file whose checksum differs from the pinned one fails the test.</summary>
    [Fact]
    public async Task FilesThatShareANameStayApartAsync()
    {
        PinnedArtifact jar = new(new Uri(InjectAddress), InjectSha256, fileName: "server.jar");
        PinnedArtifact sources = new(new Uri(SourcesAddress), SourcesSha256, fileName: "server.jar");

        string jarPath = await jar.GetPathAsync(TestContext.Current.CancellationToken);
        string sourcesPath = await sources.GetPathAsync(TestContext.Current.CancellationToken);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            static () => new PinnedArtifact(new Uri(SourcesAddress), InjectSha256, fileName: "other.jar").GetPathAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal(expected: "server.jar", Path.GetFileName(jarPath));
        Assert.Equal(expected: "server.jar", Path.GetFileName(sourcesPath));
        Assert.NotEqual(Path.GetDirectoryName(jarPath), Path.GetDirectoryName(sourcesPath));
        Assert.Equal(expected: 2497, new FileInfo(jarPath).Length);
        Assert.Equal(expected: 10928, new FileInfo(sourcesPath).Length);
        Assert.Contains(expectedSubstring: "not the pinned", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A helper that reads a private field, calls a method with a descriptor the jar's class does not declare, and calls a class that exists nowhere does not link, with one line for each; a call that a JDK supertype answers and one that the jar answers link.
    /// </summary>
    /// <param name="jdk">The JDK whose classes the helper may use.</param>
    [Theory]
    [JdkData(8, 17)]
    public async Task HelpersThatDoNotLinkFailAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string jar = await new MavenArtifact(MavenCentral, Inject, InjectSha256).GetPathAsync(TestContext.Current.CancellationToken);

        using Vault vault = new();

        // static void use(Provider provider, StringBuilder text, Vault vault) { provider.get(); text.length(); vault.secret; provider.get() as String; Missing.run(); }
        JavaClassDefinition helper = JavaClassDefinition.FromBytes(
            new ClassFileWriter(name: "agent/Helper")
                .AddMethod(
                    name: "use",
                    descriptor: "(Ljavax/inject/Provider;Ljava/lang/StringBuilder;Lapp/Vault;)V",
                    static code => code.ALoad(slot: 0)
                        .Invoke(Get)
                        .Pop()
                        .ALoad(slot: 1)
                        .Invoke(JavaMethod.Virtual(owner: "java/lang/StringBuilder", name: "length", descriptor: "()I"))
                        .Pop()
                        .ALoad(slot: 2)
                        .GetField(owner: "app/Vault", name: "secret", descriptor: "I")
                        .Pop()
                        .ALoad(slot: 0)
                        .Invoke(JavaMethod.Interface(owner: "javax/inject/Provider", name: "get", descriptor: "()Ljava/lang/String;"))
                        .Pop()
                        .Invoke(JavaMethod.Static(owner: "com/acme/Missing", name: "run", descriptor: "()V"))
                        .Return()
                )
                .ToArray()
        );

        XunitException failure = await Assert.ThrowsAnyAsync<XunitException>(() => ClassFileAssert.LinksAsync(jdk, helper, [jar, vault.Path], cancellationToken: TestContext.Current.CancellationToken));
        string[] lines = failure.Message.Split(Environment.NewLine);

        Assert.Equal(expected: 4, lines.Length);
        Assert.Contains(lines, static line => line.Contains(value: "app/Vault.secret:I, which is private in app/Vault", StringComparison.Ordinal));
        Assert.Contains(
            lines,
            static line => line.Contains(
                value: "javax/inject/Provider.get()Ljava/lang/String;, which neither the class path nor the JDK declares",
                StringComparison.Ordinal
            )
        );
        Assert.Contains(
            lines,
            static line => line.Contains(value: "com/acme/Missing.run()V, but com/acme/Missing is neither on the class path nor in the JDK", StringComparison.Ordinal)
        );
    }

    /// <summary>A helper links once a patch opens the private field it reads, or adds the getter it calls instead: the class path is checked as the patches leave it.</summary>
    /// <param name="jdk">The JDK whose classes the helper may use.</param>
    [Theory]
    [JdkData(8, 17)]
    public async Task MembersThatPatchesOpenOrAddLinkAsync(Jdk jdk)
    {
        ArgumentNullException.ThrowIfNull(jdk);
        string jar = await new MavenArtifact(MavenCentral, Inject, InjectSha256).GetPathAsync(TestContext.Current.CancellationToken);

        using Vault vault = new();

        JavaClassDefinition helper = JavaClassDefinition.FromBytes(
            new ClassFileWriter(name: "agent/Helper")
                .AddMethod(
                    name: "read",
                    descriptor: "(Lapp/Vault;)I",
                    static code => code.ALoad(slot: 0).GetField(owner: "app/Vault", name: "secret", descriptor: "I").ALoad(slot: 0).Invoke(JavaMethod.Virtual(owner: "app/Vault", name: "agent$secret", descriptor: "()I")).Op(Opcodes.IAdd).IReturn()
                )
                .ToArray()
        );

        ClassPatch opened = ClassPatch.For(className: "app/Vault").SetFieldAccess(name: "secret", accessFlags: 0x0001).AddFieldGetter(field: "secret", name: "agent$secret");

        await ClassFileAssert.LinksAsync(jdk, helper, [jar, vault.Path], [opened], TestContext.Current.CancellationToken);

        XunitException unpatched = await Assert.ThrowsAnyAsync<XunitException>(() => ClassFileAssert.LinksAsync(jdk, helper, [jar, vault.Path], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(expectedSubstring: "app/Vault.agent$secret()I", unpatched.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pinned jar is downloaded once and cached, and a patch applied across it returns one result per class it targets: the class it names, every class of a package, and the reason for each class it skips.
    /// </summary>
    /// <param name="artifact">The pinned jar.</param>
    [Theory]
    [MavenArtifactData(MavenCentral, Inject, InjectSha256)]
    public async Task PatchesApplyAcrossAPinnedJarAsync(MavenArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        string jar = await artifact.GetPathAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<ClassPatchResult> named = ClassPatch.For(className: "javax/inject/Provider").SetAccess(accessFlags: 0x0601).ApplyToJar(jar);
        IReadOnlyList<ClassPatchResult> package = ClassPatch.ForPackage(package: "javax/inject").AddFieldGetter(field: "value", name: "agent$value").ApplyToJar(jar);

        Assert.Equal(jar, await artifact.GetPathAsync(TestContext.Current.CancellationToken));
        Assert.Equal(expected: "javax.inject-1.jar", Path.GetFileName(jar));
        Assert.True(Assert.Single(named).Changed);
        Assert.Equal(expected: 6, package.Count);
        Assert.All(package, static result => Assert.Equal(PatchSkipCategory.InterfaceClass, result.SkipCategory));
    }

    /// <summary>A file pinned by its address alone runs a patch across it like a Maven artifact does, with the cached name the theory gives it.</summary>
    /// <param name="artifact">The pinned jar.</param>
    [Theory]
    [PinnedArtifactData(InjectAddress, InjectSha256, FileName = "inject-1.jar")]
    public async Task PinnedAddressesServeTheoriesAsync(PinnedArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        string jar = await artifact.GetPathAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected: "inject-1.jar", Path.GetFileName(jar));
        Assert.Equal(expected: 6, ClassPatch.ForPackage(package: "javax/inject").SetAccess(accessFlags: 0x2601).ApplyToJar(jar).Count);
    }

    /// <summary>An address that is not HTTP or HTTPS, a malformed checksum and a file name with a directory in it are refused when the artifact is pinned.</summary>
    [Fact]
    public void RefusesWhatCannotBePinned()
    {
        Assert.True(
            Assert.Throws<ArgumentException>(static () => new PinnedArtifact(new Uri(uriString: "file:///tmp/app.jar"), InjectSha256)).Message.Contains(value: "HTTP", StringComparison.Ordinal)
        );
        Assert.True(
            Assert.Throws<ArgumentException>(static () => new PinnedArtifact(new Uri(InjectAddress), sha256: "abc")).Message.Contains(value: "SHA-256", StringComparison.Ordinal)
        );
        Assert.True(
            Assert.Throws<ArgumentException>(static () => new PinnedArtifact(new Uri(InjectAddress), InjectSha256, fileName: "dir/app.jar")).Message.Contains(value: "file name", StringComparison.Ordinal)
        );
    }

    // A class directory with app/Vault, whose int field secret is private.
    private sealed class Vault : IDisposable
    {
        internal Vault()
        {
            Path = Directory.CreateTempSubdirectory(prefix: "jvmbridge-vault-").FullName;
            string package = Directory.CreateDirectory(System.IO.Path.Combine(Path, path2: "app")).FullName;

            File.WriteAllBytes(
                System.IO.Path.Combine(package, path2: "Vault.class"),
                new ClassFileWriter(name: "app/Vault").AddField(name: "secret", descriptor: "I").ToArray()
            );
        }

        internal string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

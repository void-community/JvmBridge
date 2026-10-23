using JvmBridge.Agents;
using JvmBridge.ClassFiles;

using Xunit;
using Xunit.Sdk;

namespace JvmBridge.Testing;

/// <summary>Assertions on class-file bytes, such as the output of a <c>ClassPatch</c>, that need no JVM.</summary>
public static class ClassFileAssert
{
    /// <summary>Asserts that a method contains a call of another method.</summary>
    /// <param name="classBytes">The class-file bytes.</param>
    /// <param name="method">The name of the method that should contain the call.</param>
    /// <param name="owner">The internal name of the class the call references, for example <c>java/util/Collections</c>.</param>
    /// <param name="name">The called method's name.</param>
    /// <param name="descriptor">The called method's descriptor, or <see langword="null"/> to accept any. When given, the call's whole member reference must match.</param>
    public static void Calls(byte[] classBytes, string method, string owner, string name, string? descriptor = null)
    {
        MemberReference[] calls = CallsIn(classBytes, method);

        if (!calls.Any(call => Matches(call, owner, name, descriptor)))
            Assert.Fail($"{method} does not call {owner}.{name}{descriptor}. It calls: {Describe(calls)}.");
    }

    /// <summary>Asserts that a method does not contain a call of another method.</summary>
    /// <param name="classBytes">The class-file bytes.</param>
    /// <param name="method">The name of the method that should not contain the call.</param>
    /// <param name="owner">The internal name of the class the call references.</param>
    /// <param name="name">The called method's name.</param>
    /// <param name="descriptor">The called method's descriptor, or <see langword="null"/> to reject any. When given, only a call with this exact descriptor fails the assertion.</param>
    public static void DoesNotCall(byte[] classBytes, string method, string owner, string name, string? descriptor = null)
    {
        if (CallsIn(classBytes, method).Any(call => Matches(call, owner, name, descriptor)))
            Assert.Fail($"{method} still calls {owner}.{name}{descriptor}.");
    }

    /// <summary>
    /// Asserts that a helper links: every class, field and method it refers to exists, with that descriptor, in the classes of a class path or the JDK, walking superclasses and interfaces as the JVM does, and is accessible from the helper. The
    /// application's classes are read with the given patches applied, so the members they add (<c>AddFieldGetter</c>, <c>AddInvoker</c>) or open (<c>SetFieldAccess</c>, <c>SetMethodAccess</c>) count. A stub that declares a member the real class lacks,
    /// or with another type, fails here instead of with <c>NoSuchMethodError</c> or <c>NoSuchFieldError</c> on the version that differs.
    /// </summary>
    /// <param name="jdk">The JDK whose classes the helper may use; it runs a small program to look them up.</param>
    /// <param name="helper">The helper, such as <c>JavaClasses.Acme.Agent.Network</c>, with its nested classes.</param>
    /// <param name="classPath">Jars and class directories of the application, for example from <see cref="MavenArtifact.GetPathAsync"/>; their own dependencies need not be there.</param>
    /// <param name="patches">The patches the agent registers, or none.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that fails the test with one line per reference that does not link.</returns>
    public static async Task LinksAsync(
        Jdk jdk,
        JavaClassDefinition helper,
        IEnumerable<string> classPath,
        IEnumerable<ClassPatch>? patches = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(jdk);
        ArgumentNullException.ThrowIfNull(helper);
        ArgumentNullException.ThrowIfNull(classPath);

        IReadOnlyList<string> problems = await HelperLinks.FindProblemsAsync(jdk, helper, classPath, patches ?? [], cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (problems.Count > 0)
            Assert.Fail($"{helper.Name} does not link:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// Asserts that the disassembly of a class, or of one of its methods, equals a stored snapshot. A relative path is resolved against the test project's directory, the nearest folder above the test assembly that holds a <c>.csproj</c> file (or the current directory when there is none), so snapshots live in the source tree next to the tests and are found the same way on every machine. When the snapshot is
    /// missing or different, the disassembly is written next to it as <c>&lt;snapshot&gt;.received</c> and the assertion fails; set <c>JVMBRIDGE_UPDATE_SNAPSHOTS=1</c> to accept what a run produces and write it as the snapshot instead.
    /// </summary>
    /// <param name="classBytes">The class-file bytes, for example the <c>Bytes</c> of a patch result.</param>
    /// <param name="snapshot">The snapshot file.</param>
    /// <param name="method">A method to disassemble, by name or as <c>name(descriptor)</c>, or <see langword="null"/> for the whole class.</param>
    public static void MatchesSnapshot(byte[] classBytes, string snapshot, string? method = null)
    {
        ArgumentNullException.ThrowIfNull(classBytes);
        ArgumentException.ThrowIfNullOrEmpty(snapshot);
        string path = Path.IsPathRooted(snapshot) ? snapshot : Path.Combine(FindProjectDirectory(), snapshot);
        string received = ClassFileDisassembler.Disassemble(classBytes, method).ReplaceLineEndings(replacementText: "\n");

        if (Environment.GetEnvironmentVariable(variable: "JVMBRIDGE_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".").Refresh();
            File.WriteAllText(path, received);

            return;
        }

        string? expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings(replacementText: "\n") : null;

        if (expected == received)
        {
            File.Delete(path + ".received");

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".").Refresh();
        File.WriteAllText(path + ".received", received);
        Assert.Fail(
            expected is null ? $"There is no snapshot at {path}. The disassembly was written to {path}.received; copy it over or set JVMBRIDGE_UPDATE_SNAPSHOTS=1." : $"The disassembly differs from the snapshot {path}: {FirstDifference(expected, received)}. The received text is in {path}.received."
        );
    }

    private static MemberReference[] CallsIn(byte[] classBytes, string method)
    {
        ArgumentNullException.ThrowIfNull(classBytes);
        JavaClassFile file = JavaClassFile.Read(classBytes);
        ClassMember member = file.FindMethod(method) ?? throw new XunitException($"The class {file.Name} has no method {method}.");
        CodeAttribute code = member.Code ?? throw new XunitException($"{method} has no code.");

        return
        [
            .. code.Instructions
                .Where(instruction => instruction.ConstantIndex is not null && instruction.Opcode is >= 0xb6 and <= 0xb9)
                .Select(instruction => file.GetMemberReference(instruction.ConstantIndex ?? 0))
        ];
    }

    private static string Describe(MemberReference[] calls)
    {
        return calls.Length == 0 ? "nothing" : string.Join(separator: ", ", calls.Select(static call => call.Owner + "." + call.Name + call.Descriptor));
    }

    private static string FindProjectDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles(searchPattern: "*.csproj").Any())
                return directory.FullName;

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string FirstDifference(string expected, string received)
    {
        string[] expectedLines = expected.Split(separator: '\n');
        string[] receivedLines = received.Split(separator: '\n');

        for (int index = 0; index < Math.Max(expectedLines.Length, receivedLines.Length); index++)
        {
            string expectedLine = index < expectedLines.Length ? expectedLines[index] : "(end)";
            string receivedLine = index < receivedLines.Length ? receivedLines[index] : "(end)";

            if (expectedLine != receivedLine)
                return $"line {(index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)} expected \"{expectedLine.Trim()}\" but was \"{receivedLine.Trim()}\"";
        }

        return "the texts differ";
    }

    private static bool Matches(MemberReference call, string owner, string name, string? descriptor)
    {
        return call.Owner == owner && call.Name == name && (descriptor is null || call.Descriptor == descriptor);
    }
}

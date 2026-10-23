# Testing an agent

## JvmBridge.Testing

`JvmBridge.Testing` is a companion package for xUnit v3 that removes the test harness an agent usually builds for itself. Add it to a test project with `dotnet add package JvmBridge.Testing`:

```csharp
[Theory]
[JdkData(8, 17)]
public async Task AgentChangesTheSort(Jdk jdk)
{
    string classPath = await jdk.CompileAsync("Fixtures/**/*.java");
    JavaRun without = await jdk.RunAsync("example.Target", classPath);
    JavaRun with = await jdk.RunAsync("example.Target", classPath, PublishedAgent.Find("MyAgent"));

    Assert.Contains("[a, b, c]", without.Output);
    Assert.Contains("[c, b, a]", with.Output);
}
```

- `[JdkData]` supplies the checksum-pinned JDK of each major version from this repository's inventory, downloaded once and cached under the temporary directory. Set `JVMBRIDGE_JDK_<major>` to use an installed JDK instead, or `JVMBRIDGE_JDK_<major>_HOTSPOT` and `JVMBRIDGE_JDK_<major>_OPENJ9` to name one per implementation; those win over the shared variable. An installed JDK is asked for its version and implementation once per run (`java -XshowSettings:properties -version`), and a test fails when it is another Java or another implementation than the test asked for, or when the variable names a directory that does not exist, so a test labelled Java 8 never passes on another Java. A version with no pinned JDK for the platform skips the test with the reason.
- `CompileAsync` compiles Java sources for Java 8 unless you ask for another release. `[JdkData(6, 7)]` runs on the pinned Zulu 6 and 7 (see [**Java 6 and 7**](old-java.md)), and `release: 6` needs a JDK from 6 to 11, `release: 7` one from 7 to 19.
- `RunAsync` returns the exit code and combined output and stops a run that exceeds its timeout.
- `PublishedAgent.Find` looks in `artifacts/agent/<rid>` (or `JVMBRIDGE_AGENT_DIRECTORY`) and skips the test when the agent has not been published for the platform.
- `PublishedAgent.FindJar` finds the jar that a publish with `JvmBridgeAgentJar` writes beside the library, in the same places. `RunAsync` loads a jar with `-javaagent:` and a library with `-agentpath:`, with `JavaRunOptions.AgentOptions` after the `=` either way, and preloads HotSpot's `libjsig` only for a library. Run the same JVM test through both to cover both routes:

  ```csharp
  JavaRun library = await jdk.RunAsync("example.Target", classPath, PublishedAgent.Find("MyAgent"));
  JavaRun jar = await jdk.RunAsync("example.Target", classPath, PublishedAgent.FindJar("MyAgent"));
  ```
- `ClassFileAssert.MatchesSnapshot(bytes, "Snapshots/run.txt", method: "run")` compares the disassembly of a class or method with a stored snapshot. The text is `javap -c`-style and deterministic (`ClassFileDisassembler.Disassemble` produces it), so a patch's effect on the bytecode shows up in a diff. A relative path is resolved against the test project's directory; a mismatch writes `<snapshot>.received` next to it (add `*.received` to `.gitignore`), and `JVMBRIDGE_UPDATE_SNAPSHOTS=1` accepts what a run produced.
- `ClassFileAssert.Calls` and `DoesNotCall` check patched class bytes without a JVM. Give them a `descriptor` to match the call's whole member reference rather than only its owner and name.
- `MavenArtifact` pins a jar of the application by checksum and caches it like a JDK; `[MavenArtifactData(repository, "group:artifact:version", sha256)]` supplies one per theory row, so a test runs against every version a patch has to handle:

  ```csharp
  [Theory]
  [MavenArtifactData("https://maven.minecraftforge.net", "net.minecraftforge:eventbus:0.10.3", "<sha256>")]
  [MavenArtifactData("https://maven.minecraftforge.net", "net.minecraftforge:eventbus:0.10.5", "<sha256>")]
  public async Task PatchesTheVersionsThatNeedItAsync(MavenArtifact eventBus)
  {
      string jar = await eventBus.GetPathAsync(TestContext.Current.CancellationToken);

      IReadOnlyList<ClassPatchResult> results = ListenerPatch.ApplyToJar(jar);

      Assert.All(results, result => Assert.True(result.Changed, result.SkippedReason));
  }
  ```

  Coordinates take an optional `:classifier` and `@extension`. A download whose checksum differs fails the test.
- `patch.ApplyToJar(path)` (in the JvmBridge package) applies a patch to every class of a jar that it targets and returns one `ClassPatchResult` each, with the reason for each class it skips.
- `ClassFileAssert.LinksAsync(jdk, helper, classPath, patches)` checks that a helper links against the real classes: every class, field and method it refers to must exist with that descriptor on the class path or in the JDK, found through superclasses and interfaces as the JVM finds them, and be accessible from the helper. The class path is read as the agent's patches leave it, so a getter or invoker they add, or a member they open, counts. A stub source that gets a member wrong fails here, with one line per reference, rather than as `NoSuchMethodError` on the version that differs. The application's classes are read from their class files, so their own dependencies need not be on the class path; the JDK's are looked up by a small Java program on `jdk`.

Test patches on class bytes first (`patch.Apply(bytes)` returns a `ClassPatchResult` that says what changed, or why nothing did), and run a JVM only for behavior the bytes cannot show. Patch tests need no JDK, no published agent and no native toolchain.

## The agent template

`dotnet new install JvmBridge.Templates` adds the `jvmbridge-agent` template. `dotnet new jvmbridge-agent -n MyAgent` creates:

- an agent project with a placeholder `ClassPatch` and a Java helper, which builds on a machine with no JDK because the template sets `JvmBridgeProvisionJdk`;
- a test project that checks the patch on class bytes and runs a fixture in JVMs without the agent, with its library and with its jar;
- a GitHub Actions workflow that publishes the agent and its jar for each platform and runs the tests; when you push a `v*` tag it combines the jars into one with `JvmBridgeMergeAgentJars` and attaches that jar and an archive per platform to a release.

The JVM tests skip until the agent is published for your platform with `dotnet publish MyAgent.csproj -c Release -r <rid> --self-contained -o artifacts/agent/<rid>`, which writes the library and, because the template sets `JvmBridgeAgentJar`, the jar.

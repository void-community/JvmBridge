# JvmBridge.Testing

Test helpers for [JvmBridge](https://github.com/void-community/JvmBridge) agents, for xUnit v3.

- `[JdkData(8)]` supplies a `Jdk` for each requested Java major. The JDK is the checksum-pinned archive from JvmBridge's own inventory, downloaded once and cached; set `JVMBRIDGE_JDK_<major>` (or `JVMBRIDGE_JDK_<major>_HOTSPOT`, `JVMBRIDGE_JDK_<major>_OPENJ9`) to use an installed one. An installed JDK must be the Java version and implementation the test asks for, which it is asked once per run; otherwise, or when the directory does not exist, the test fails and says what it found. A major with no pinned JDK for the platform skips the test with the reason. Java 6 and 7 are pinned Zulu builds for linux-x64 and win-x64, and Java 7 also for win-x86 and osx-x64; `CompileAsync(sources, release: 6)` needs a JDK from 6 to 11, and `release: 7` one from 7 to 19.
- `jdk.CompileAsync("Fixtures/**/*.java")` compiles Java sources with the JDK and returns the class path.
- `jdk.RunAsync("pkg.Main", classPath, agent: PublishedAgent.Find("MyAgent"))` runs a class, optionally with the agent, and returns the exit code and combined output.
- `PublishedAgent.Find(name)` finds a published agent for the current runtime identifier in `artifacts/agent/<rid>` (or `JVMBRIDGE_AGENT_DIRECTORY`) and skips the test when it has not been published.
- `ClassFileAssert.MatchesSnapshot(bytes, "Snapshots/run.txt", method: "run")` compares the `javap -c`-style disassembly of a class or method with a stored snapshot, so a patch's effect on the bytecode is reviewed in a diff. A relative path is resolved against the test project's directory. A mismatch writes `<snapshot>.received` next to it (add `*.received` to `.gitignore`), and `JVMBRIDGE_UPDATE_SNAPSHOTS=1` accepts what a run produced.
- `ClassFileAssert.Calls` and `DoesNotCall` assert on patched class bytes without a JVM. Pass a `descriptor` to match the whole member reference, not only the owner and name.

# JvmBridge.Testing

Test helpers for [JvmBridge](https://github.com/void-community/JvmBridge) agents, for xUnit v3.

- `[JdkData(8)]` supplies a `Jdk` for each requested Java major. The JDK is the checksum-pinned archive from JvmBridge's own inventory, downloaded once and cached; set `JVMBRIDGE_JDK_<major>` to use an installed one. A major with no pinned JDK for the platform skips the test with the reason.
- `jdk.CompileAsync("Fixtures/**/*.java")` compiles Java sources with the JDK and returns the class path.
- `jdk.RunAsync("pkg.Main", classPath, agent: PublishedAgent.Find("MyAgent"))` runs a class, optionally with the agent, and returns the exit code and combined output.
- `PublishedAgent.Find(name)` finds a published agent for the current runtime identifier in `artifacts/agent/<rid>` (or `JVMBRIDGE_AGENT_DIRECTORY`) and skips the test when it has not been published.
- `ClassFileAssert.Calls` and `DoesNotCall` assert on patched class bytes without a JVM.

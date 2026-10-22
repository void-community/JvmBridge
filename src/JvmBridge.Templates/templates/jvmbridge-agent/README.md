# MyJvmAgent

A NativeAOT JVM agent built with [JvmBridge](https://github.com/void-community/JvmBridge).

- `PatcherAgent.cs` registers a helper class and a declarative `ClassPatch`. `Patches.SortDescending` is a placeholder: change the class, method and call it targets.
- `java/` holds Java helper classes. They are compiled with `javac` when you build, so set `JAVA_HOME` (or `JvmBridgeJdkHome`) to a JDK 9 or later.
- `tests/MyJvmAgent.Tests` checks the patch on class bytes, then runs a fixture in a JVM with and without the agent. The JVM tests skip until the agent is published for your platform.

```sh
dotnet publish MyJvmAgent.csproj -c Release -r <rid> --self-contained -o artifacts/agent/<rid>
dotnet test tests/MyJvmAgent.Tests
```

`.github/workflows/ci.yml` publishes the agent for each platform, runs the tests, and attaches the agents to a release when you push a `v*` tag. Load an agent with `java -agentpath:<path to MyJvmAgent.so|.dll|.dylib> ...`.

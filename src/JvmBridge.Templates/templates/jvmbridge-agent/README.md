# MyJvmAgent

A NativeAOT JVM agent built with [JvmBridge](https://github.com/void-community/JvmBridge).

- `PatcherAgent.cs` registers a helper class and a declarative `ClassPatch`. `Patches.SortDescending` is a placeholder: change the class, method and call it targets.
- `java/` holds Java helper classes. They are compiled with `javac` when you build. The build uses the JDK in `JvmBridgeJdkHome` or `JAVA_HOME`, and downloads a pinned JDK once (`JvmBridgeProvisionJdk`) when neither names one.
- `tests/MyJvmAgent.Tests` checks the patch on class bytes, then runs a fixture in a JVM without the agent, with its library and with its jar. The JVM tests skip until the agent is published for your platform.

```sh
dotnet publish MyJvmAgent.csproj -c Release -r <rid> --self-contained -o artifacts/agent/<rid>
dotnet test tests/MyJvmAgent.Tests
```

A publish writes the native library (`MyJvmAgent.so`, `.dll` or `.dylib`) and `MyJvmAgent.jar`, a jar that holds that library (`JvmBridgeAgentJar` in the project). Load the agent either way:

```sh
java -agentpath:/absolute/path/MyJvmAgent.so -jar app.jar   # the platform's library, at JVM startup
java -javaagent:MyJvmAgent.jar -jar app.jar                 # one file, the form build tools and containers take
```

The jar starts the agent after the JVM has started, as an attach, so it gets `OnAttach` and the capabilities of an attached agent; see [**Loading and attaching**](https://github.com/void-community/JvmBridge/blob/main/docs/guide/attaching.md).

`.github/workflows/ci.yml` publishes the agent for each platform and runs the tests. On Linux it links the agent against the glibc 2.17 sysroot of .NET's cross-build image (the `glibc-sysroot` action of [void-community/Actions](https://github.com/void-community/Actions)), so the agent loads on Ubuntu 20.04, Debian 11, RHEL 7 and 8 and later; a publish on your own machine needs that machine's glibc or newer, see [**Publishing for Linux**](https://github.com/void-community/JvmBridge/blob/main/docs/guide/publishing.md). When you push a `v*` tag it combines the jars of all platforms into one `MyJvmAgent.jar` with the `JvmBridgeMergeAgentJars` target of the JvmBridge package, and attaches that jar and an archive per platform to a release.

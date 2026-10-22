# JvmBridge.Tool

The `jvmbridge` command, a .NET tool that ships next to the [JvmBridge](https://www.nuget.org/packages/JvmBridge) package.

```bash
dotnet tool install --global JvmBridge.Tool
```

## `jvmbridge dump`

Disassembles a class in the style of `javap -c`, from a class file or from a jar:

```bash
jvmbridge dump Service.class --method handle
jvmbridge dump app.jar                       # lists the classes
jvmbridge dump app.jar --class com/acme/Service
```

## `jvmbridge patch --dry-run`

Previews what the patches of an agent would do to class files, without a JVM. The tool runs the agent's `Configure` against an offline JVM, then applies the patches it registered to the classes you give it:

```bash
jvmbridge patch --dry-run --agent bin/Release/net10.0/MyAgent.dll --classes app.jar --disassemble
```

Only patches registered in `Configure` are visible; patches an agent registers later, and custom `TransformClass` overrides, are not previewed. Use a tool version at least as new as the JvmBridge package the agent was built with.

## `jvmbridge pack`

Packs the published native libraries of an agent, and the jars of single platforms, into one jar that `java -javaagent` starts on any of its platforms:

```bash
jvmbridge pack -o agent.jar --agent linux-x64=publish/linux-x64/MyAgent.so --agent win-x64=publish/win-x64/MyAgent.dll
jvmbridge pack -o agent.jar --jar linux-x64/MyAgent.jar --jar linux-arm64/MyAgent.jar
java -javaagent:agent.jar=log=debug -jar app.jar
```

The jar carries a small launcher class that extracts the library of the running platform into a private directory under `java.io.tmpdir` and loads it; that directory must be writable. The agent then starts as if it had been attached to the running JVM, with the text after `=` as its options. A JVM that finds no library for its platform stops at startup with a message that names the platforms the jar has.

A project that builds with the JvmBridge package can write these jars without the tool: `<JvmBridgeAgentJar>true</JvmBridgeAgentJar>` makes `dotnet publish` write a one-platform jar beside the library, and the `JvmBridgeMergeAgentJars` target combines such jars.

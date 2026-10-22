# The jvmbridge command

`jvmbridge` is a .NET tool that ships beside the JvmBridge package. It reads and previews what an agent does, and packs agents for delivery. It needs the .NET 10 runtime, not the JVM.

```sh
dotnet tool install --global JvmBridge.Tool
```

Its version is the version of the JvmBridge package it was built with; install a version at least as new as the package your agent uses.

## `jvmbridge dump`

Disassembles a class in the style of `javap -c`, from a class file or a jar. The output is the one `ClassFileDisassembler` and `ClassFileAssert.MatchesSnapshot` use, so what you read here is what a snapshot test compares.

```sh
jvmbridge dump Service.class --method handle
jvmbridge dump app.jar                         # lists the classes of the jar
jvmbridge dump app.jar --class com/acme/Service
```

## `jvmbridge patch --dry-run`

Previews what the patches of an agent would do to class files, without a JVM:

```sh
jvmbridge patch --dry-run --agent bin/Release/net10.0/MyAgent.dll --classes app.jar --disassemble
```

The tool loads the managed assembly of the agent (the `.dll` that `dotnet build` writes, before it is compiled to native code), creates its one `JavaAgent`, runs `Configure` against an offline JVM, and applies the patches that `Configure` registered to the classes you give it: `.class` files, directories of them, or jars. For each class it prints the patches that matched and what each did or why it did nothing, then how many classes would change. `--options` passes the agent options and `--disassemble` prints every patched class.

The offline JVM offers every capability and accepts every subscription; any other call into the JVM fails with an error the agent can catch. Three things are not previewed: patches an agent registers later (in `OnLoad`, `OnAttach` or `OnCommand`), custom `TransformClass` overrides, and anything that depends on which classes the JVM has loaded. A `ForSubclassesOf` matcher sees the supertypes of the classes you give it, so pass the application's classes together.

## `jvmbridge pack`

Packs the published native libraries of an agent, and the libraries of other agent jars, into one jar for `java -javaagent`; see [**From a jar with `-javaagent`**](attaching.md#from-a-jar-with--javaagent).

```sh
jvmbridge pack -o agent.jar --agent linux-x64=publish/linux-x64/MyAgent.so --agent win-x64=publish/win-x64/MyAgent.dll
jvmbridge pack -o agent.jar --jar linux-x64/MyAgent.jar --jar linux-arm64/MyAgent.jar
```

`--agent` takes `<rid>=<path>` and `--jar` the path of a jar that `jvmbridge pack` or a publish with `JvmBridgeAgentJar` wrote; both may be repeated and mixed. The runtime identifiers are `linux-x64`, `linux-arm64`, `linux-arm`, `linux-musl-x64`, `linux-musl-arm64`, `win-x64`, `win-x86`, `win-arm64`, `osx-x64` and `osx-arm64`. The same library twice for a platform counts once, and two different ones are refused. The jar is byte-for-byte the same for the same libraries, however they were given. A project that builds with the JvmBridge package can do the same without the tool, with the `JvmBridgeMergeAgentJars` target.

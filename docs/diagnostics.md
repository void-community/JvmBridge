# Diagnostics

Every error the JvmBridge package reports carries a `JVMB` code. The generator reports JVMB001 to JVMB003 as compiler errors with a source location where one exists; the build targets and the JDK task report the rest during `dotnet build`. Each entry gives the cause and what to change.

| Code | Reported by | Meaning |
|---|---|---|
| [**JVMB001**](#jvmb001-invalid-jvm-agent) | generator | The agent declaration or configuration is invalid |
| [**JVMB002**](#jvmb002-invalid-native-helper-class) | generator | A `[JavaClass]` class is invalid |
| [**JVMB003**](#jvmb003-unsupported-native-helper-method-signature) | generator | A `[JavaMethod]` signature is not supported |
| [**JVMB004**](#jvmb004-no-jdk-for-javasource) | build | `JavaSource` items but no JDK |
| [**JVMB005**](#jvmb005-javac-not-found) | build | The JDK directory has no `javac` |
| [**JVMB006**](#jvmb006-publishaot-missing) | build | An agent project does not set `PublishAot` |
| [**JVMB007**](#jvmb007-no-pinned-jdk) | build task | `JvmBridgeProvisionJdk` has no pinned JDK for this machine |
| [**JVMB008**](#jvmb008-jdk-download-failed) | build task | The pinned JDK could not be downloaded or verified |
| [**JVMB009**](#jvmb009-provisioning-needs-dotnet-build) | build | A task that needs `dotnet build` was used outside it |
| [**JVMB010**](#jvmb010-agent-must-be-a-library) | build | An agent project is not a class library |
| [**JVMB011**](#jvmb011-javareference-has-no-types) | build task | A `JavaReference` has no `Types` metadata |
| [**JVMB012**](#jvmb012-types-entry-matches-nothing) | build task | A `Types` entry of a `JavaReference` matches no class |
| [**JVMB013**](#jvmb013-javareference-cannot-be-read) | build task | A `JavaReference` jar is missing or unreadable |
| [**JVMB014**](#jvmb014-agent-jar-cannot-be-written) | build task | The `-javaagent` jar of `JvmBridgeAgentJar` or `JvmBridgeMergeAgentJars` cannot be written |
| [**JVMB015**](#jvmb015-no-jars-to-merge) | build | `JvmBridgeMergeAgentJars` found no jar to combine |

### JVMB001: Invalid JVM agent

The generator emits `Agent_OnLoad`, `Agent_OnAttach` and `Agent_OnUnload` for exactly one agent class per native library, and reports one of these messages when it cannot:

- *Exactly one concrete JavaAgent subclass is required per native library.* Declare one agent, or none in a library that only holds shared code.
- *Agent must derive from JavaAgent, be non-generic and non-abstract, and have an accessible parameterless constructor.* The runtime creates the agent itself.
- *Set `<JvmBridgeAgent>true</JvmBridgeAgent>` in the agent project.* The property turns on the generated entry points.
- *Agent entry points are generated; remove conflicting manual exports.* Delete your own `[UnmanagedCallersOnly(EntryPoint = "Agent_OnLoad")]` (or the attach and unload twins).

### JVMB002: Invalid native helper class

A class marked `[JavaClass("pkg/Name")]` must have a Java internal name with slashes (`pkg/Callbacks`, not `pkg.Callbacks`), be a top-level, non-generic `static partial` class, and contain at least one method marked `[JavaMethod]`.

### JVMB003: Unsupported native helper method signature

A `[JavaMethod]` method must be `static`, non-generic and non-async, and use only parameter and return types the generator can marshal: `bool`, `sbyte`, `char`, `short`, `int`, `long`, `float`, `double`, `string`, one-dimensional arrays of the primitives (`byte[]`, `int[]` and so on) and of `string`, and `nint` (give its Java type with `[JavaType]`). The first parameter may be a `JavaEnvironment`. `ref`, `in` and `out` parameters are rejected. The message names the method, the parameter and the type.

### JVMB004: No JDK for JavaSource

The project has `JavaSource` items, which are compiled with `javac`, but neither `JvmBridgeJdkHome` nor `JAVA_HOME` names a directory. Set one to a JDK 9 or later, or set `<JvmBridgeProvisionJdk>true</JvmBridgeProvisionJdk>` to download a checksum-pinned JDK once per machine.

### JVMB005: javac not found

The JDK directory exists but has no `bin/javac`, which usually means it is a JRE. Point `JvmBridgeJdkHome` at a JDK. A `JvmBridgeJdkHome` you set is never replaced by a download, even with `JvmBridgeProvisionJdk`.

### JVMB006: PublishAot missing

An agent project (`JvmBridgeAgent` true) must set `<PublishAot>true</PublishAot>` itself, next to `JvmBridgeAgent`. The .NET SDK decides on NativeAOT while it evaluates the project, before the package can act, so without it `dotnet publish` succeeds but produces only managed assemblies and no native library. Passing `-p:PublishAot=true` on the command line also satisfies the check.

### JVMB007: No pinned JDK

`JvmBridgeProvisionJdk` is on, but the inventory has no available JDK of `JvmBridgeJdkMajor` (default 17, or `JavaRelease` when that is higher) for this machine's runtime identifier. The message names the version, implementation, platform and the inventory's reason. Set `JvmBridgeJdkHome` or `JAVA_HOME` to an installed JDK instead.

### JVMB008: JDK download failed

The pinned JDK archive could not be downloaded, its checksum did not match, or it could not be unpacked. Check the network and proxy settings, or set `JvmBridgeJdkHome` to an installed JDK to avoid the download. A checksum failure is never retried with an unchecked download.

### JVMB009: Provisioning needs dotnet build

`JvmBridgeProvisionJdk` downloads the JDK, `JavaReference` reads jars, and `JvmBridgeAgentJar` and `JvmBridgeMergeAgentJars` write `-javaagent` jars, with tasks that only load under the `dotnet` CLI's MSBuild. Build with `dotnet build`, `dotnet publish` or `dotnet msbuild`; for the JDK you can also set `JvmBridgeJdkHome` or `JAVA_HOME` so no download is needed.

### JVMB010: Agent must be a library

An agent is a NativeAOT shared library that a JVM loads with `-agentpath`, so its project must set `<OutputType>Library</OutputType>`.

### JVMB011: JavaReference has no Types

A `JavaReference` item names a jar, and its `Types` metadata selects the classes to wrap in typed proxies: `<JavaReference Include="libs/app.jar" Types="com/acme/Service;com/acme/model/*" />`. An entry is an internal class name, a package (`com/acme/*`) or a package and its subpackages (`com/acme/**`), separated by semicolons. Without `Types` nothing is wrapped, because wrapping a whole jar would generate far more code than an agent calls.

### JVMB012: Types entry matches nothing

A `Types` entry matched no class in the jar, which is usually a typo or a package written with dots instead of slashes. Nested classes (names with `$`) are never wrapped, and a class that is not public is skipped.

### JVMB013: JavaReference cannot be read

The jar does not exist at that path (the path is relative to the project) or is not a readable zip file.

### JVMB014: Agent jar cannot be written

`JvmBridgeAgentJar` writes a `-javaagent` jar after `dotnet publish`, and `JvmBridgeMergeAgentJars` combines such jars; the message says what stopped them. A library or a jar that does not exist, a jar that no JvmBridge publish or `jvmbridge pack` wrote, a runtime identifier that the launcher cannot detect, and two different libraries for one platform among the jars to combine are refused. The same library twice for a platform is fine.

### JVMB015: No jars to merge

`JvmBridgeMergeAgentJars` combines the jars named by `JvmBridgeAgentJars`, paths or wildcards relative to the project (separated by `%3B` on a command line), and none matched: `-p:JvmBridgeAgentJars="agents/*/MyAgent.jar"`. Check where the jars were downloaded to.

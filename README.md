# JvmBridge

[![CI](https://img.shields.io/github/actions/workflow/status/void-community/JvmBridge/runnable-commit.yml?branch=main&style=for-the-badge)](https://github.com/void-community/JvmBridge/actions/workflows/runnable-commit.yml)
[![License](https://img.shields.io/github/license/void-community/JvmBridge?style=for-the-badge)](LICENSE)

**Write JVM agents in C#. Generate the native bindings. Test the actual binaries.**

JvmBridge combines generated JNI/JVMTI declarations, explicit Java reference ownership, and NativeAOT agent entry-point generation in one NuGet package. It works with ordinary Java applications and provides a standalone JVM-hosting API for .NET applications.

JvmBridge is published on [**NuGet**](https://www.nuget.org/packages/JvmBridge), with companion packages: [**JvmBridge.Testing**](https://www.nuget.org/packages/JvmBridge.Testing) for xUnit tests, [**JvmBridge.Templates**](https://www.nuget.org/packages/JvmBridge.Templates) for a `dotnet new` agent template and [**JvmBridge.Logging**](https://www.nuget.org/packages/JvmBridge.Logging) for `Microsoft.Extensions.Logging` adapters. To start without writing a project file, run `dotnet new install JvmBridge.Templates`, then `dotnet new jvmbridge-agent -n MyAgent`.

## A native agent in a few lines

Create a .NET 10 class library and reference JvmBridge:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <JvmBridgeAgent>true</JvmBridgeAgent>
    <PublishAot>true</PublishAot>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="JvmBridge" Version="26.10.22.2272" />
  </ItemGroup>
</Project>
```

`dotnet add package JvmBridge` adds the latest version; pin the one you test against. `PublishAot` is explicit because the SDK decides on NativeAOT while it evaluates the project, before the package's build assets can act on `JvmBridgeAgent`, so the package cannot imply it. Leaving it out fails the build with JVMB006; without that check `dotnet publish` would succeed and produce only managed assemblies, and no native library.

```csharp
using JvmBridge.Agents;
using JvmBridge.Runtime;

public sealed class MyAgent : JavaAgent
{
    public override void OnLoad(AgentContext context)
    {
        context.Log("loaded with options: " + context.Options);
    }

    public override void OnVmInit(AgentContext context, JavaEnvironment environment)
    {
        using JavaLocalReference text = environment.NewString("Hello from C#!");
        context.Log(environment.GetString(text.Handle));
    }
}
```

No attribute or manually exported entry point is required. The generator finds the single concrete `JavaAgent` subclass and emits `Agent_OnLoad`, `Agent_OnAttach`, and `Agent_OnUnload` into your assembly. Override `Configure` to request capabilities before events are installed. `OnAttach` runs after callbacks are active, so it can immediately retransform existing classes. Abstract agent base classes are allowed; ambiguous or invalid agents produce compiler errors. `context.Log` writes to standard error without `Console`, which needs libicu to initialize.

Publish for the **JVM process's** operating system and architecture:

```sh
dotnet publish -c Release -r linux-arm64
java -agentpath:/absolute/path/MyAgent.so=hello -jar application.jar
```

Windows uses `.dll`; macOS uses `.dylib`; Linux uses `.so`. Native publishing requires the platform's C/C++ toolchain. The resulting library includes its .NET runtime; Java users do not need to install .NET. To load an agent through `JAVA_TOOL_OPTIONS` or into a running JVM, see [**Loading and attaching**](docs/guide/attaching.md).

On Unix HotSpot, use the JDK's signal-chaining library when hosting both runtimes in one process. For a typical Linux JDK:

```sh
LD_PRELOAD="$JAVA_HOME/lib/libjsig.so" java -agentpath:/absolute/path/MyAgent.so -jar application.jar
```

Locations differ across distributions and Java versions; Java 8 commonly places the library beneath `jre/lib/<architecture>`. macOS uses `DYLD_INSERT_LIBRARIES` with `libjsig.dylib`. The test runner discovers the correct library. Do not inject HotSpot's library into OpenJ9.

## Capabilities

- **Generated native API:** JNI/JVMTI types, constants, function-table entries, and callback signatures from pinned OpenJDK headers. C# raw declarations live in `JvmBridge.Native` and preserve native identifiers.
- **Runtime helpers:** JVM creation, thread attachment, UTF-16 string conversion, modified UTF-8 names/options, checked field access, constructor and typed method calls, native registration, and owned local/global references.
- **Agent lifecycle:** startup and late attachment, VM initialization/death, class preparation, copied JVMTI class/member metadata, capability negotiation, and retransformation.
- **Class files and patches:** a read-only class-file model, an editor whose edits keep every constant index and code offset, a writer for small helper classes, and declarative `ClassPatch`es with results a test can assert on, all without a JVM.
- **Helper classes:** written in Java (`JavaSource`, compiled with `javac`, with an optional pinned JDK download for machines that have none) or in C# (`[JavaClass]` and `[JavaMethod]`).
- **Events and JVM state:** thread, class-load, exception, method, garbage-collection and monitor events with capability negotiation, thread and stack wrappers, and class redefinition.
- **Java interop:** arrays, exception throwing, monitors and local frames, `[JavaMethod]` with arrays, and typed proxies for the classes of a jar.
- **Logging and options:** levels, a replaceable sink, an `ILogger` adapter and a parsed option string.
- **Testing:** `JvmBridge.Testing` supplies checksum-pinned JDKs, `javac` and `java` runners, published-agent lookup and class-file assertions.
- **One package:** the runtime, source generator, and MSBuild integration ship together. Consumers do not need Clang or header files.
- **Real validation:** CI publishes the example against the packed NuGet package and loads it into each available pinned JVM.

## Documentation

- [**User guide**](docs/guide/index.md): [**concepts**](docs/guide/concepts.md), [**patching classes**](docs/guide/patching.md), [**recipes**](docs/guide/recipes.md), [**loading and attaching**](docs/guide/attaching.md), [**JVM events**](docs/guide/events.md), [**calling between C# and Java**](docs/guide/interop.md), [**testing**](docs/guide/testing.md), [**troubleshooting**](docs/guide/troubleshooting.md), [**limitations**](docs/guide/limitations.md) and [**stability and versioning**](docs/guide/stability.md).
- [**Diagnostics**](docs/diagnostics.md): every `JVMB` build error and what to change.
- [**Compatibility**](docs/compatibility.md): the generated inventory of JVMs and platforms.
- [**Footprint**](docs/footprint.md): what loading an agent costs in library size, start-up time and memory.
- [**Contributing**](docs/contributing.md): building, testing and maintaining JvmBridge itself.

## Compatibility

The manifest tracks every NativeAOT target in scope and every Java major from **8 through 26**, with HotSpot and OpenJ9 inventories. Archive availability varies by version and architecture. Mobile targets and platforms without a suitable JVM are explicitly experimental or unavailable.

See the [**generated compatibility inventory**](docs/compatibility.md). It shows available downloads, not a blanket support claim. The matching commit's [**CI reports**](https://github.com/void-community/JvmBridge/actions) show which native builds and runtime tests actually passed. Historical patch releases are not exhaustively tested. JVM agent support does not follow automatically from .NET target support.

## Examples

- [**HelloAgent**](samples/HelloAgent): emits a Java 8-compatible native callback helper in C#, registers native methods, preserves Unicode across threads/global references, transforms fixture method bodies, and supports late attachment.
- [**JavaHost**](samples/JavaHost): creates a JVM from an explicit native-library path, invokes Java, handles exceptions, and demonstrates ownership.

See [**automation setup**](docs/automation.md) and [**agent instructions**](AGENTS.md). Original code is [**MIT licensed**](LICENSE); see [**upstream notices**](NOTICE.md) for header provenance.

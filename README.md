# JvmBridge

[![CI](https://img.shields.io/github/actions/workflow/status/void-community/JvmBridge/runnable-commit.yml?branch=main&style=for-the-badge)](https://github.com/void-community/JvmBridge/actions/workflows/runnable-commit.yml)
[![License](https://img.shields.io/github/license/void-community/JvmBridge?style=for-the-badge)](LICENSE)

**Write JVM agents in C#. Generate the native bindings. Test the actual binaries.**

JvmBridge combines generated JNI/JVMTI declarations, explicit Java reference ownership, and NativeAOT agent entry-point generation in one NuGet package. It works with ordinary Java applications and provides a standalone JVM-hosting API for .NET applications.

The initial package is available as a [**CI artifact**](https://github.com/void-community/JvmBridge/actions). Public NuGet publication is configured but has not yet been performed.

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
    <PackageReference Include="JvmBridge" Version="YOUR_PACKAGE_VERSION" />
  </ItemGroup>
</Project>
```

Use the exact version from the downloaded `.nupkg`, or a published version once available. Restore a downloaded package from its local directory together with nuget.org. `PublishAot` is explicit because the SDK evaluates native runtime-pack requirements before package build targets.

```csharp
using JvmBridge.Agents;
using JvmBridge.Runtime;

public sealed class MyAgent : JavaAgent
{
    public override void OnLoad(AgentContext context)
    {
        Console.Error.WriteLine("Agent loaded: " + context.Options);
    }

    public override void OnVmInit(AgentContext context, JavaEnvironment environment)
    {
        using JavaLocalReference text = environment.NewString("Hello from C#!");
        Console.Error.WriteLine(environment.GetString(text.Handle));
    }
}
```

No attribute or manually exported entry point is required. The generator finds the single concrete `JavaAgent` subclass and emits `Agent_OnLoad`, `Agent_OnAttach`, and `Agent_OnUnload` into your assembly. Abstract agent base classes are allowed; ambiguous or invalid agents produce compiler errors.

Publish for the **JVM process's** operating system and architecture:

```sh
dotnet publish -c Release -r linux-arm64
java -agentpath:/absolute/path/MyAgent.so=hello -jar application.jar
```

Windows uses `.dll`; macOS uses `.dylib`; Linux uses `.so`. Native publishing requires the platform's C/C++ toolchain. The resulting library includes its .NET runtime; Java users do not need to install .NET.

On Unix HotSpot, use the JDK's signal-chaining library when hosting both runtimes in one process. For a typical Linux JDK:

```sh
LD_PRELOAD="$JAVA_HOME/lib/libjsig.so" java -agentpath:/absolute/path/MyAgent.so -jar application.jar
```

Locations differ across distributions and Java versions; Java 8 commonly places the library beneath `jre/lib/<architecture>`. macOS uses `DYLD_INSERT_LIBRARIES` with `libjsig.dylib`. The test runner discovers the correct library. Do not inject HotSpot's library into OpenJ9. The fixtures test both Java exceptions and successful process shutdown.

## Capabilities

- **Generated native API:** JNI/JVMTI types, constants, function-table entries, and callback signatures from pinned OpenJDK headers. C# raw declarations live in `JvmBridge.Native` and preserve native identifiers.
- **Runtime helpers:** JVM creation, thread attachment, UTF-16 string conversion, modified UTF-8 names/options, method calls, native registration, and owned local/global references.
- **Agent lifecycle:** startup and late attachment, VM initialization/death, class preparation, class-file transformation, capability negotiation, and retransformation.
- **One package:** the runtime, source generator, and MSBuild integration ship together. Consumers do not need Clang or header files.
- **Real validation:** CI publishes the example against the packed NuGet package and loads it into each available pinned JVM.

Raw function pointers are intentionally unsafe escape hatches. Check version/capability/phase requirements before using them. Variadic and va_list entries are exposed as addresses; use typed argument-array `*A` calls from managed code. JvmBridge supplies class bytes and JVMTI transformation plumbing, not a general Java bytecode editor.

## Compatibility

The manifest tracks every NativeAOT target in scope and every Java major from **8 through 26**, with HotSpot and OpenJ9 inventories. Archive availability varies by version and architecture. Mobile targets and platforms without a suitable JVM are explicitly experimental or unavailable.

See the [**generated compatibility inventory**](docs/compatibility.md). It shows available downloads, not a blanket support claim. The matching commit's [**CI reports**](https://github.com/void-community/JvmBridge/actions) show which native builds and runtime tests actually passed. Historical patch releases are not exhaustively tested. JVM agent support does not follow automatically from .NET target support.

## Examples

- [**HelloAgent**](samples/HelloAgent): registers native methods, preserves Unicode across threads/global references, transforms a fixture class, and supports late attachment.
- [**JavaHost**](samples/JavaHost): creates a JVM from an explicit native-library path, invokes Java, handles exceptions, and demonstrates ownership.

Agent callbacks borrow their JNI environment. Local references must be disposed before the callback returns. Promote a reference to a global reference before retaining it or passing it to another attached thread. Attachments only detach threads they attached. Dispose JVM-owned resources before destroying a hosted JVM. Generated entry points retain the native module until process termination, including when a JVM releases its own agent-library handle.

## Build and test

Install the SDK from `global.json`, Python 3.12+, and the native publishing toolchain:

```sh
dotnet tool restore
python eng/check.py
dotnet pack src/JvmBridge -c Release -o artifacts/packages -p:Version=0.1.0-local.1
python eng/test_agent.py --rid linux-x64 --version 0.1.0-local.1
```

The last command builds the actual package consumers and executes the entire pinned target-specific JVM matrix, downloading one JDK at a time. It includes startup, late attachment, native exports, ABI comparisons, JNI calls, Unicode, references, native threads, transformation/retransformation, failure containment, and shutdown.

For a focused run with an installed JDK:

```sh
python eng/test_agent.py --rid linux-arm64 --version 0.1.0-local.1 --jdk-home /path/to/jdk --java 25
```

Logs, native/managed ABI reports, and per-cell results are written to `artifacts/results`. `python eng/report.py --verify` requires all available manifest cells to have passed; it is intended for the aggregated CI run. Do not treat a subset run as full coverage.

## Reproducible maintenance

```sh
python eng/generate.py --check
python eng/abi.py --check
python eng/report.py --check
```

Generation verifies header checksums and uses a fixed Clang target and shim headers to avoid host-dependent declarations. The shims cover only unused stdio declarations and Clang's target-specific va_list. Upstream headers are unmodified. Native C probes independently validate the actual target ABI, including capability bits and callback/function-table offsets.

Scheduled JDK maintenance discovers stable releases, pins archive checksums, regenerates bindings, and opens tested update PRs. Renovate maintains the SDK, packages, tools, and actions. Compatible updates may merge after protected checks pass. Public API changes and new platforms require review. Release-please owns versions, release notes, and tags.

See [**contributing**](CONTRIBUTING.md), [**automation setup**](docs/automation.md), and [**agent instructions**](AGENTS.md). Original code is [**MIT licensed**](LICENSE); see [**upstream notices**](NOTICE.md) for header provenance.

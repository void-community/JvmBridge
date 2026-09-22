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

No attribute or manually exported entry point is required. The generator finds the single concrete `JavaAgent` subclass and emits `Agent_OnLoad`, `Agent_OnAttach`, and `Agent_OnUnload` into your assembly. Override `Configure` to request capabilities before events are installed. `OnAttach` runs after callbacks are active, so it can immediately retransform existing classes. Abstract agent base classes are allowed; ambiguous or invalid agents produce compiler errors.

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

- [**HelloAgent**](samples/HelloAgent): emits a Java 8-compatible native callback helper in C#, registers native methods, preserves Unicode across threads/global references, transforms fixture method bodies, and supports late attachment.
- [**JavaHost**](samples/JavaHost): creates a JVM from an explicit native-library path, invokes Java, handles exceptions, and demonstrates ownership.

For NativeAOT executables that embed a JVM, set `<JvmBridgeHost>true</JvmBridgeHost>`. On Intel macOS this reserves a 1 MiB null guard rather than the default low 4 GiB reservation, leaving room for OpenJ9 compressed-reference metadata. On Windows AMD64 it opts the host executable out of CET shadow-stack compatibility because HotSpot CPU feature probes are incompatible with it; no operating-system mitigation policy is changed. An explicit `CETCompat` property takes precedence. The JavaHost sample includes this setting.

Agent callbacks borrow their JNI environment. Local references must be disposed before the callback returns. Promote a reference to a global reference before retaining it or passing it to another attached thread. Attachments only detach threads they attached. Dispose JVM-owned resources before destroying a hosted JVM. JNI failures preserve their result in `JniException.ErrorCode`; some OpenJ9 builds return `JNI_ERR` from `DestroyJavaVM` even in a plain C host. The test reports record this native baseline explicitly rather than claiming successful embedded shutdown. Checked-JNI warnings are compared with an uninstrumented Java launch; additional agent warnings fail validation. Generated entry points retain the native module until process termination, including when a JVM releases its own agent-library handle. `OnUnload` performs exactly-once logical cleanup at VM death, with the native unload export as a fallback.

Override `TransformClass(AgentContext, ClassFile, ClassTransformContext)` to inspect the defining `Loader`, `ProtectionDomain`, and `ClassBeingRedefined` JNI references. Zero means a bootstrap loader, missing protection domain, or initial definition, respectively; `ClassFile.Name` can also be null. The original two-argument override continues to work. Use `callback.CanUseJni` before accessing `callback.Environment`: the class-file hook can run before JNI is permitted, including with a null environment in the primordial phase. `IsSameObject` compares loader identity, not class names. All callback references, the environment, and class bytes belong to the current callback and thread; do not retain them or share them across threads. While JNI is permitted, use `environment.NewLocalReference(handle).ToGlobal()` to retain a nonzero reference, and dispose the global while its JVM is alive. Callbacks may be reentrant, so avoid shared callback-local state. Phase restrictions still apply to other JVMTI/JNI operations, and a pending Java exception is left untouched rather than invoking the transformer.

To inject calls into an application without shipping native declarations in its classes, generate a Java class file (HelloAgent emits its minimal Java 8 helper entirely in C#), then call `environment.DefineClass("jvmbridge/sample/AgentCallbacks", loader.Handle, bytes)` and `environment.RegisterNative(helper.Handle, "onFrame", "(Ljava/lang/Object;)V", callbackPointer)`. `DefineClass` returns a disposable, thread-owned local class reference and clears Java exceptions before raising `JavaException`; attempting to define the same name again in the same loader fails. Its loader may be zero for bootstrap definition where the JVM permits that operation; checked JNI on OpenJ9 requires an explicit loader. HelloAgent installs the helper once per defining loader, using the system loader during `OnVmInit` for startup or before retransformation during `OnAttach`, and separately installing it in each independent parentless fixture loader before its class is transformed. It skips instrumenting its own helper. Never call ordinary JNI from `Agent_OnLoad`, transform application calls before registering the native method, or retain callback-local references. Late attachment rewrites existing method bodies only; retransformation cannot introduce new application methods or fields. Unmanaged callbacks contain all exceptions and execute with the originating Java thread's JNI environment.

## Build and test

Install the .NET 11 RC1 SDK, the .NET 10 runtime, and the native publishing toolchain. CI pins its SDK versions in the workflows; local builds use your installed SDK. Pack the library before restoring the solution, because the samples consume that package:

```sh
export VersionTimestamp="$(date -u '+%y %m %d %H %M %S')"
package_version="$(dotnet msbuild src/JvmBridge/JvmBridge.csproj -getProperty:PackageVersion)"
dotnet tool restore --tool-manifest src/JvmBridge.Build/.config/dotnet-tools.json
dotnet build src/JvmBridge.Build -c Release -t:VerifyGenerated -p:RestoreLockedMode=true
dotnet pack src/JvmBridge -c Release -o artifacts/packages
dotnet restore JvmBridge.slnx --locked-mode
dotnet build JvmBridge.slnx -c Release --no-restore
dotnet test JvmBridge.slnx -c Release --no-build --no-restore
dotnet publish samples/HelloAgent -c Release -r linux-x64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -o artifacts/agent/linux-x64
dotnet publish samples/JavaHost -c Release -r linux-x64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -o artifacts/host/linux-x64
TARGET_RID=linux-x64 dotnet test tests/JvmBridge.IntegrationTests -c Release
```

MSBuild publishes standalone consumers from the packed artifact; xUnit executes each pinned JVM cell as a separate test, downloading one JDK at a time. Native tests are explicitly skipped in ordinary solution test runs unless `TARGET_RID` is set. CI compiles portable Java 8 fixture bytecode once with `dotnet build src/JvmBridge.Build -c Release -t:CompileFixtures -p:RestoreLockedMode=true` and runs it unchanged on all target JVMs; local runs can compile fixtures with the supplied JDK. Coverage includes startup, late attachment, native exports, ABI comparisons, JNI calls, Unicode, references, native threads, transformation/retransformation, failure containment, and shutdown.

For a focused run with an installed JDK:

```sh
dotnet publish samples/HelloAgent -c Release -r linux-arm64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -o artifacts/agent/linux-arm64
dotnet publish samples/JavaHost -c Release -r linux-arm64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -o artifacts/host/linux-arm64
TARGET_RID=linux-arm64 JVM_JDK_HOME=/path/to/jdk JVM_JAVA_MAJOR=25 dotnet test tests/JvmBridge.IntegrationTests -c Release
```

Set `NATIVE_COMPILER` when the C compiler is not `cc` (`cl` on Windows). Logs, native/managed ABI reports, and per-cell results are written to `artifacts/results`. `dotnet build src/JvmBridge.Build -c Release -t:VerifyCoverage -p:RestoreLockedMode=true` requires all available manifest cells to have passed; it is intended for the aggregated CI run. Do not treat a subset run as full coverage.

## Reproducible maintenance

```sh
dotnet build src/JvmBridge.Build -c Release -t:Generate -p:RestoreLockedMode=true
dotnet build src/JvmBridge.Build -c Release -t:VerifyGenerated -p:RestoreLockedMode=true
```

Generation verifies header checksums and uses a fixed Clang target and shim headers to avoid host-dependent declarations. The shims cover only unused stdio declarations and Clang's target-specific va_list. Upstream headers are unmodified. Native C probes independently validate the actual target ABI, including capability bits and callback/function-table offsets.

Scheduled JDK maintenance discovers stable releases, pins archive checksums, regenerates bindings, and opens tested update PRs. Renovate maintains the SDK, packages, tools, and actions. Compatible updates may merge after protected checks pass. Public API changes and new platforms require review.

Every validated commit to `main` publishes its tested NuGet package using Trusted Publishing. Versions follow [**NetAgents' timestamp scheme**](https://github.com/caunt/NetAgents): `YY.M.D.B`, with a UTC time-of-day bucket from 1000 through 9999. CI freezes the timestamp once and uses the same version throughout packaging, testing, and publishing. There are no release PRs or tags; the publisher never rebuilds the package.

Both samples belong to the solution and inherit NetAgents and central package management. They use package references, never project references. The local feed is configured in their project files. Set `<JvmBridgeAbiInspector>true</JvmBridgeAbiInspector>` to generate the ABI inspector during compilation from the referenced package's native types; no inspector source is checked in.

See [**automation setup**](docs/automation.md) and [**agent instructions**](AGENTS.md). Original code is [**MIT licensed**](LICENSE); see [**upstream notices**](NOTICE.md) for header provenance.

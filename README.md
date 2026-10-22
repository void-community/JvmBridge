# JvmBridge

[![CI](https://img.shields.io/github/actions/workflow/status/void-community/JvmBridge/runnable-commit.yml?branch=main&style=for-the-badge)](https://github.com/void-community/JvmBridge/actions/workflows/runnable-commit.yml)
[![License](https://img.shields.io/github/license/void-community/JvmBridge?style=for-the-badge)](LICENSE)

**Write JVM agents in C#. Generate the native bindings. Test the actual binaries.**

JvmBridge combines generated JNI/JVMTI declarations, explicit Java reference ownership, and NativeAOT agent entry-point generation in one NuGet package. It works with ordinary Java applications and provides a standalone JVM-hosting API for .NET applications.

JvmBridge is published on [**NuGet**](https://www.nuget.org/packages/JvmBridge), with two companion packages: [**JvmBridge.Testing**](https://www.nuget.org/packages/JvmBridge.Testing) for xUnit tests and [**JvmBridge.Templates**](https://www.nuget.org/packages/JvmBridge.Templates) for a `dotnet new` agent template. To start without writing a project file, run `dotnet new install JvmBridge.Templates`, then `dotnet new jvmbridge-agent -n MyAgent`.

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

`dotnet add package JvmBridge` adds the latest version; pin the one you test against. `PublishAot` is explicit because the SDK evaluates native runtime-pack requirements before package build targets.

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
- **Runtime helpers:** JVM creation, thread attachment, UTF-16 string conversion, modified UTF-8 names/options, checked field access, constructor and typed method calls, native registration, and owned local/global references.
- **Agent lifecycle:** startup and late attachment, VM initialization/death, class preparation, copied JVMTI class/member metadata, capability negotiation, and retransformation.
- **Class files:** a read-only class-file model, an editor whose edits keep every constant index and code offset, and a writer for small helper classes, in `JvmBridge.ClassFiles`.
- **One package:** the runtime, source generator, and MSBuild integration ship together. Consumers do not need Clang or header files.
- **Real validation:** CI publishes the example against the packed NuGet package and loads it into each available pinned JVM.

Raw function pointers are intentionally unsafe escape hatches. Check version/capability/phase requirements before using them. Variadic and va_list entries are exposed as addresses; use typed argument-array `*A` calls from managed code. JvmBridge supplies class bytes, JVMTI transformation plumbing and the small class-file model below, not a general Java bytecode editor.

`JvmBridge.ClassFiles` reads, edits and writes class files without a JVM. `JavaClassFile.Read` exposes the version, flags, hierarchy, fields and methods, the typed constant pool and each `Code` attribute's instructions (offset, opcode, constant operand). It does not interpret stack maps, and it throws `ClassFormatException` for truncated input, trailing bytes, unknown constant tags and malformed code. `ClassFileEditor` only makes edits that keep every existing constant index and code offset, so code, exception tables and stack map frames stay valid without being recomputed: new constants are appended once, a member reference can be retargeted everywhere, an invocation can be replaced in place, constants can be replaced, and access flags can be changed.

```csharp
using JvmBridge.ClassFiles;

JavaClassFile file = JavaClassFile.Read(bytes);
foreach (ClassMember method in file.Methods)
    Console.WriteLine($"{file.Name}.{method.Name}{method.Descriptor}");

ClassFileEditor editor = new(bytes);
int redirected = editor.ReplaceInvocation(
    JavaMethod.Static("java/util/Collections", "sort", "(Ljava/util/List;Ljava/util/Comparator;)V"),
    toOwner: "legacyforgepatcher/Collections");
byte[] patched = editor.ToArray();
```

`ReplaceInvocation` turns `invokestatic`, `invokevirtual` or `invokeinterface` into `invokestatic` of another owner at the same length; a virtual or interface receiver becomes the first parameter, and `invokeinterface` padding becomes `nop`. `ClassFileWriter` writes small classes made of static methods (including `native` ones for `RegisterNative`) with a deduplicating constant pool; straight-line code needs no stack map frames. `ModifiedUtf8.GetBytes` encodes class-file strings without the JNI terminator.

`ClassPatch` describes edits to one class declaratively: `ClassPatch.For(className)` is immutable and reusable, `.Method(name, descriptor)` scopes the edits that follow to one method (`.AnyMethod()` ends the scope), and `.RedirectInvocation`, `.ReplaceConstant`, `.SetAccess`/`.SetFieldAccess` and the `.Edit(editor => ...)` escape hatch add edits. `patch.Apply(bytes)` returns a `ClassPatchResult` that says what changed, or why nothing did, so a test can assert on it without a JVM. A patch is all or nothing: when its method or invocation is missing, the class is left unchanged and the result names the reason.

```csharp
public sealed class PatcherAgent : JavaAgent
{
    internal static readonly ClassPatch TweakSort = ClassPatch.For("cpw/mods/fml/relauncher/CoreModManager")
        .Method("sortTweakList", "()V")
        .RedirectInvocation(JavaMethod.Static("java/util/Collections", "sort", "(Ljava/util/List;Ljava/util/Comparator;)V"), toOwner: "legacyforgepatcher/Collections");

    public override void Configure(AgentContext context)
    {
        byte[] helperBytes = File.ReadAllBytes("Collections.class"); // or build one with ClassFileWriter
        context.DefineClass(JavaClassDefinition.FromBytes(helperBytes), ClassLoaderTarget.Bootstrap);
        context.Patch(TweakSort);
    }
}
```

Register patches and helpers from `Configure`. The runtime only calls a patch for its class name, applies it as the class loads, logs `patched ...` or `skipped ...: reason` through `AgentContext.Log`, and, when the agent attaches late and retransformation is available, retransforms the matching classes that are already loaded. `ClassLoaderTarget.Bootstrap` defines the helpers in the bootstrap loader at VM initialization (or at attach), so every loader sees them. OpenJ9's checked JNI treats defining a class with a null loader as fatal, so there the helpers go in a temporary jar on the bootstrap search path instead; on HotSpot that would disable class-data sharing for application classes. `ClassLoaderTarget.PatchedClassLoader` defines the helpers once in each loader that defines a patched class, before the first patched class from that loader, for helpers that must see application classes; it cannot serve classes of the bootstrap loader, which the log reports. `AgentContext.Log` writes lines prefixed with the agent's assembly name to standard error. It is safe in every callback and never throws. It writes the file descriptor directly because `Console` initializes terminal and globalization state, which aborts the process on a host without libicu; failures that the runtime contains are reported the same way.

Helper classes can be written in Java or in C#. For Java, add `JavaSource` items to the agent project:

```xml
<ItemGroup>
  <JavaSource Include="java/**/*.java" />
</ItemGroup>
```

The build compiles them with `javac --release $(JavaRelease)` (8 unless you set `JavaRelease`) using the JDK in `JvmBridgeJdkHome` or `JAVA_HOME`, and fails with JVMB004 or JVMB005 when neither names a JDK (version 9 or later) that contains `javac`. The class files are embedded, and each top-level class becomes an `internal` `JavaClassDefinition` named after its package, with its inner and anonymous classes defined along with it: `context.DefineClass(JavaClasses.Legacyforgepatcher.Collections, ClassLoaderTarget.Bootstrap)` for `legacyforgepatcher/Collections.java`.

For C#, mark a `static partial` class with `[JavaClass("pkg/Name")]` and its static methods with `[JavaMethod]`. The generator emits a Java class that declares them `native` (named like the C# method with a lower-case first letter, or by `[JavaMethod("name")]`), the unmanaged entry points that call your methods, and a `Definition` property that registers them when the class is defined:

```csharp
[JavaClass("legacyforgepatcher/Callbacks")]
internal static partial class Callbacks
{
    [JavaMethod]
    internal static string Greet(JavaEnvironment environment, string name) => "Hello, " + name;
}

// In Configure:
context.DefineClass(Callbacks.Definition, ClassLoaderTarget.Bootstrap);
```

An optional first `JavaEnvironment` parameter receives the callback's JNI environment. Other parameters and the return type may be `bool`, `sbyte`, `char`, `short`, `int`, `long`, `float`, `double`, `string`, or `nint` for a Java object (typed by `[JavaType("Ljava/util/List;")]`, otherwise `java/lang/Object`). An exception that a method throws is reported and contained, and the JVM receives a default value.

`JvmBridge.Testing` is a companion package for xUnit v3 that removes the test harness an agent usually builds for itself. Add it to a test project with `dotnet add package JvmBridge.Testing`:

```csharp
[Theory]
[JdkData(8, 17)]
public async Task AgentChangesTheSort(Jdk jdk)
{
    string classPath = await jdk.CompileAsync("Fixtures/**/*.java");
    JavaRun without = await jdk.RunAsync("example.Target", classPath);
    JavaRun with = await jdk.RunAsync("example.Target", classPath, PublishedAgent.Find("MyAgent"));

    Assert.Contains("[a, b, c]", without.Output);
    Assert.Contains("[c, b, a]", with.Output);
}
```

`[JdkData]` supplies the checksum-pinned JDK of each major version from this repository's inventory, downloaded once and cached; set `JVMBRIDGE_JDK_<major>` to use an installed JDK instead, and a version with no pinned JDK for the platform skips the test with the reason. `CompileAsync` compiles Java sources for Java 8 unless you ask for another release, `RunAsync` returns the exit code and combined output (and stops a run that exceeds its timeout), and `PublishedAgent.Find` looks in `artifacts/agent/<rid>` and skips the test when the agent has not been published for the platform. `ClassFileAssert.Calls` and `DoesNotCall` check patched class bytes without a JVM.

`dotnet new install JvmBridge.Templates` adds the `jvmbridge-agent` template: `dotnet new jvmbridge-agent -n MyAgent` creates an agent project with a placeholder `ClassPatch` and a Java helper, a test project that checks the patch on class bytes and runs a fixture in JVMs with and without the agent, and a GitHub Actions workflow that publishes the agent for each platform, runs the tests, and attaches the agents to a release when you push a `v*` tag.

In a live JVM callback, use a class reference from that callback or an explicitly owned reference to inspect members and invoke Java code:

```csharp
foreach (JavaFieldInfo field in context.GetDeclaredFields(type))
    Console.WriteLine($"{field.Name}: {field.Descriptor}");

nint fieldId = environment.GetField(type, "profile", "Ljava/lang/Object;");
using JavaLocalReference profile = environment.GetObjectField(instance, fieldId);
nint constructor = environment.GetMethod(type, "<init>", "()V");
using JavaLocalReference created = environment.NewObject(type, constructor, ReadOnlySpan<JvmBridge.Native.jvalue>.Empty);
```

`GetDeclaredMethods` and `GetDeclaredFields` inspect prepared classes in the live JVMTI phase. They copy names, descriptors, modifiers and nullable generic signatures into managed records, and release JVMTI allocations even on failure. These records expose raw IDs, which do **not** retain their declaring class or loader: never cache an ID beyond its declaring class's lifetime, and keep classes from independent loaders distinct even when names and descriptors match. `GetSuperclass`, `GetObjectClass` and `AgentContext.GetClassLoader` return disposable local references; a bootstrap loader, root superclass or null object field has an empty reference (`Handle == 0`). Dispose nonempty locals before the callback or attachment scope ends. Missing members and Java-thrown exceptions use the checked `JavaException` path: it captures `JavaTypeName`, `JavaMessage` and `JavaStackTrace` (rendered to at most eight throwables and 64 frames per throwable), then clears the pending JNI exception. These nullable managed snapshots retain no Java reference; if extraction fails, the original `Operation` remains available and secondary exceptions are cleared. Unmanaged callbacks must still contain managed exceptions rather than propagate them into Java. Perform ordinary JNI calls only in phases where the JVM permits them, not during `Agent_OnLoad`.

## Compatibility

The manifest tracks every NativeAOT target in scope and every Java major from **8 through 26**, with HotSpot and OpenJ9 inventories. Archive availability varies by version and architecture. Mobile targets and platforms without a suitable JVM are explicitly experimental or unavailable.

See the [**generated compatibility inventory**](docs/compatibility.md). It shows available downloads, not a blanket support claim. The matching commit's [**CI reports**](https://github.com/void-community/JvmBridge/actions) show which native builds and runtime tests actually passed. Historical patch releases are not exhaustively tested. JVM agent support does not follow automatically from .NET target support.

## Examples

- [**HelloAgent**](samples/HelloAgent): emits a Java 8-compatible native callback helper in C#, registers native methods, preserves Unicode across threads/global references, transforms fixture method bodies, and supports late attachment.
- [**JavaHost**](samples/JavaHost): creates a JVM from an explicit native-library path, invokes Java, handles exceptions, and demonstrates ownership.

For NativeAOT executables that embed a JVM, set `<JvmBridgeHost>true</JvmBridgeHost>`. On Intel macOS this reserves a 1 MiB null guard rather than the default low 4 GiB reservation, leaving room for OpenJ9 compressed-reference metadata. On Windows AMD64 it opts the host executable out of CET shadow-stack compatibility because HotSpot CPU feature probes are incompatible with it; no operating-system mitigation policy is changed. An explicit `CETCompat` property takes precedence. The JavaHost sample includes this setting.

On Windows x86, `JavaVirtualMachine.Create` automatically installs the packaged native compatibility handler for HotSpot. It restores fault recovery for the exact legacy `SafeFetch` probe used by JNI reference checking. The handler and JVM library stay loaded for process lifetime; unrelated memory faults retain normal Windows handling.

Agent callbacks borrow their JNI environment. Local references must be disposed before the callback returns. Promote a reference to a global reference before retaining it or passing it to another attached thread. Attachments only detach threads they attached. Dispose JVM-owned resources before destroying a hosted JVM. JNI failures preserve their result in `JniException.ErrorCode`; some OpenJ9 builds return `JNI_ERR` from `DestroyJavaVM` even in a plain C host. The test reports record this native baseline explicitly rather than claiming successful embedded shutdown. Checked-JNI warnings are compared with an uninstrumented Java launch; additional agent warnings fail validation. Generated entry points retain the native module until process termination, including when a JVM releases its own agent-library handle. `OnUnload` performs exactly-once logical cleanup at VM death, with the native unload export as a fallback.

Override `TransformClass(AgentContext, ClassFile, ClassTransformContext)` to inspect the defining `Loader`, `ProtectionDomain`, and `ClassBeingRedefined` JNI references. Zero means a bootstrap loader, missing protection domain, or initial definition, respectively; `ClassFile.Name` can also be null. The original two-argument override continues to work. Use `callback.CanUseJni` before accessing `callback.Environment`: the class-file hook can run before JNI is permitted, including with a null environment in the primordial phase. `IsSameObject` compares Java object identity, not handle values or class names: two null handles compare equal, and null differs from a live object. All callback references, the environment, and class bytes belong to the current callback and thread; do not retain them or share them across threads. While JNI is permitted, use `environment.NewLocalReference(handle).ToGlobal()` to retain a nonzero reference, and dispose the global while its JVM is alive. Callbacks may be reentrant, so avoid shared callback-local state. Phase restrictions still apply to other JVMTI/JNI operations, and a pending Java exception is left untouched rather than invoking the transformer.

To observe a Java object without keeping it alive, create an owned `JavaWeakGlobalReference` with `environment.NewWeakGlobalReference(handle)` while the handle is valid. On any attached thread of the same JVM, call `using JavaLocalReference? current = weak.TryPromote(environment)`: null means the referent was collected; a pending Java exception raises `JavaException` instead. Keep and use the promoted strong local for the entire operation, disposing it before its environment or JNI callback ends. Checking the weak handle for collection and then using that handle is racy. Dispose the weak owner explicitly, including during agent shutdown, while the JVM is still alive; it neither retains the Java referent nor extends the JVM's lifetime. A disposed weak owner cannot be promoted or accessed, and thread-local environments and locals cannot cross threads.

To inject calls into an application without shipping native declarations in its classes, generate a Java class file (HelloAgent emits its minimal Java 8 helper entirely in C#), then call `environment.DefineClass("jvmbridge/sample/AgentCallbacks", loader.Handle, bytes)` and `environment.RegisterNative(helper.Handle, "onFrame", "(Ljava/lang/Object;)V", callbackPointer)`. `DefineClass` returns a disposable, thread-owned local class reference and clears Java exceptions before raising `JavaException`; attempting to define the same name again in the same loader fails. Its loader may be zero for bootstrap definition where the JVM permits that operation; checked JNI on OpenJ9 requires an explicit loader. HelloAgent installs the helper once per defining loader, using the system loader during `OnVmInit` for startup or before retransformation during `OnAttach`, and separately installing it in each independent parentless fixture loader before its class is transformed. It skips instrumenting its own helper. Never call ordinary JNI from `Agent_OnLoad`, transform application calls before registering the native method, or retain callback-local references. Late attachment rewrites existing method bodies only; retransformation cannot introduce new application methods or fields. Unmanaged callbacks contain all exceptions and execute with the originating Java thread's JNI environment.

## Build and test

Install the .NET 11 RC1 SDK, the .NET 10 runtime, and the native publishing toolchain. Packing also compiles the Windows x86 compatibility DLL: on Ubuntu install `clang lld mingw-w64-i686-dev`; on Windows use an x86 Native Tools command prompt. Other Unix hosts need LLVM and a MinGW-w64 i686 toolchain; set `WindowsX86ToolchainRoot` to its directory if it is not `/usr/i686-w64-mingw32`. Package consumers need no additional compiler for this DLL. CI pins its SDK versions in the workflows; local builds use your installed SDK. To verify the current source through the samples, pack the library and pass that version when restoring, building, and testing the solution:

```sh
export VersionTimestamp="$(date -u '+%y %m %d %H %M %S')"
package_version="$(dotnet msbuild src/JvmBridge/JvmBridge.csproj -getProperty:PackageVersion)"
dotnet tool restore --tool-manifest src/JvmBridge.Build/.config/dotnet-tools.json
dotnet build src/JvmBridge.Build -c Release -t:VerifyGenerated -p:RestoreLockedMode=true
dotnet pack src/JvmBridge -c Release -o artifacts/packages
dotnet restore JvmBridge.slnx --locked-mode -p:JvmBridgeVersion="$package_version"
dotnet build JvmBridge.slnx -c Release --no-restore -p:JvmBridgeVersion="$package_version"
dotnet test JvmBridge.slnx -c Release --no-build --no-restore -p:JvmBridgeVersion="$package_version"
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

Both samples belong to the solution and inherit NetAgents and central package management. They use package references, never project references. Ordinary restores use the published JvmBridge baseline in `Directory.Packages.props`; the local feed is added only when `artifacts/packages` exists. CI supplies `JvmBridgeVersion` to test its freshly packed artifact. Renovate ignores JvmBridge itself to prevent publishing a package from triggering another sample dependency update and publication; update the published baseline deliberately when the examples require newer APIs. Set `<JvmBridgeAbiInspector>true</JvmBridgeAbiInspector>` to generate the ABI inspector during compilation from the referenced package's native types; no inspector source is checked in.

See [**automation setup**](docs/automation.md) and [**agent instructions**](AGENTS.md). Original code is [**MIT licensed**](LICENSE); see [**upstream notices**](NOTICE.md) for header provenance.

# Contributing

How to build, test and maintain JvmBridge itself. The conventions for agents and contributors are in [**AGENTS.md**](../AGENTS.md), and CI and publishing are described in [**automation setup**](automation.md).

## Build and test

Install the .NET 11 RC1 SDK, the .NET 10 runtime, and the native publishing toolchain. Packing also compiles the Windows x86 compatibility DLL: on Ubuntu install `clang lld mingw-w64-i686-dev`; on Windows use an x86 Native Tools command prompt. Other Unix hosts need LLVM and a MinGW-w64 i686 toolchain; set `WindowsX86ToolchainRoot` to its directory if it is not `/usr/i686-w64-mingw32`. Package consumers need no additional compiler for this DLL. CI pins its SDK versions in the workflows; local builds use your installed SDK. To verify the current source through the samples, pack the library and pass that version when restoring, building, and testing the solution:

```sh
export VersionTimestamp="$(date -u '+%y %m %d %H %M %S')"
package_version="$(dotnet msbuild src/JvmBridge/JvmBridge.csproj -getProperty:PackageVersion)"
dotnet tool restore --tool-manifest src/JvmBridge.Build/.config/dotnet-tools.json
dotnet build src/JvmBridge.Build -c Release -t:VerifyGenerated -p:RestoreLockedMode=true
dotnet pack src/JvmBridge -c Release -o artifacts/packages
dotnet pack src/JvmBridge.Testing -c Release -o artifacts/packages
dotnet pack src/JvmBridge.Logging -c Release -o artifacts/packages
dotnet pack src/JvmBridge.Templates -c Release -o artifacts/packages
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

## Footprint

`FootprintTests` in `tests/JvmBridge.IntegrationTests` measures a minimal agent (`samples/FootprintAgent`) and is skipped unless `JVMBRIDGE_FOOTPRINT=1` and `TARGET_RID` are set. Publish the sample next to it, then run the test; set `JVMBRIDGE_JDK_<major>` to use an installed JDK instead of downloading the pinned one:

```sh
dotnet publish samples/FootprintAgent -c Release -r linux-arm64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -o artifacts/footprint-agent/linux-arm64
TARGET_RID=linux-arm64 JVMBRIDGE_FOOTPRINT=1 dotnet test tests/JvmBridge.IntegrationTests -c Release --filter FullyQualifiedName~FootprintTests
```

Results go to `artifacts/footprint/<rid>/hotspot-<major>.json`. CI's `footprint` job does the same on each native runner and fails only when the library grows more than 10 percent past `docs/footprint.baseline.json`. After a deliberate change in size, download the `footprint-*` artifacts of a green run and rewrite the baseline and the published table with `dotnet run --project src/JvmBridge.Maintenance -- footprint <directory>`.

## Reproducible maintenance

```sh
dotnet build src/JvmBridge.Build -c Release -t:Generate -p:RestoreLockedMode=true
dotnet build src/JvmBridge.Build -c Release -t:VerifyGenerated -p:RestoreLockedMode=true
```

Generation verifies header checksums and uses a fixed Clang target and shim headers to avoid host-dependent declarations. The shims cover only unused stdio declarations and Clang's target-specific va_list. Upstream headers are unmodified. Native C probes independently validate the actual target ABI, including capability bits and callback/function-table offsets.

Scheduled JDK maintenance discovers stable releases, pins archive checksums, regenerates bindings, and opens tested update PRs. Renovate maintains the SDK, packages, tools, and actions. Compatible updates may merge after protected checks pass. Public API changes and new platforms require review.

Every validated commit to `main` publishes its tested NuGet package using Trusted Publishing. Versions follow [**NetAgents' timestamp scheme**](https://github.com/caunt/NetAgents): `YY.M.D.B`, with a UTC time-of-day bucket from 1000 through 9999. CI freezes the timestamp once and uses the same version throughout packaging, testing, and publishing. There are no release PRs, tags or GitHub releases; the publisher never rebuilds the package. Each package carries the subjects of the commits in its push as its release notes, written by the build workflow from the push range.

Both samples belong to the solution and inherit NetAgents and central package management. They use package references, never project references. Ordinary restores use the published JvmBridge baseline in `Directory.Packages.props`; the local feed is added only when `artifacts/packages` exists. CI supplies `JvmBridgeVersion` to test its freshly packed artifact. Renovate ignores JvmBridge itself to prevent publishing a package from triggering another sample dependency update and publication; update the published baseline deliberately when the examples require newer APIs. Set `<JvmBridgeAbiInspector>true</JvmBridgeAbiInspector>` to generate the ABI inspector during compilation from the referenced package's native types; no inspector source is checked in.

See also [**automation setup**](automation.md) and [**agent instructions**](../AGENTS.md). Original code is [**MIT licensed**](../LICENSE); see [**upstream notices**](../NOTICE.md) for header provenance.

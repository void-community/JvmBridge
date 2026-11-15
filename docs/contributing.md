# Contributing

How to build, test and maintain JvmBridge itself. The conventions for agents and contributors are in [**AGENTS.md**](../AGENTS.md), and CI and publishing are described in [**automation setup**](automation.md).

## Build and test

Install the .NET 11 SDK, the .NET 10 runtime, and the native publishing toolchain. Packing also compiles the Windows x86 compatibility DLL: on Ubuntu install `clang lld mingw-w64-i686-dev`; on Windows use an x86 Native Tools command prompt. Other Unix hosts need LLVM and a MinGW-w64 i686 toolchain; set `WindowsX86ToolchainRoot` to its directory if it is not `/usr/i686-w64-mingw32`. Package consumers need no additional compiler for this DLL. CI pins its SDK in `ci.yml`; local builds use your installed SDK. To verify the current source through the samples, pack the library and pass that version when restoring, building, and testing the solution:

```sh
export VersionTimestamp="$(date -u '+%y %m %d %H %M %S')"
package_version="$(dotnet msbuild src/JvmBridge/JvmBridge.csproj -getProperty:PackageVersion)"
dotnet tool restore --tool-manifest src/JvmBridge.Build/.config/dotnet-tools.json
dotnet build src/JvmBridge.Build -c Release -t:VerifyGenerated
dotnet pack src/JvmBridge -c Release -o artifacts/packages
dotnet pack src/JvmBridge.Testing -c Release -o artifacts/packages
dotnet pack src/JvmBridge.Logging -c Release -o artifacts/packages
dotnet pack src/JvmBridge.Templates -c Release -o artifacts/packages
dotnet restore JvmBridge.slnx -p:JvmBridgeVersion="$package_version"
dotnet build JvmBridge.slnx -c Release --no-restore -p:JvmBridgeVersion="$package_version"
dotnet test JvmBridge.slnx -c Release --no-build --no-restore -p:JvmBridgeVersion="$package_version"
dotnet publish samples/HelloAgent -c Release -r linux-x64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -o artifacts/agent/linux-x64
dotnet publish samples/JavaHost -c Release -r linux-x64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -o artifacts/host/linux-x64
TARGET_RID=linux-x64 dotnet test tests/JvmBridge.IntegrationTests -c Release
```

MSBuild publishes standalone consumers from the packed artifact; xUnit executes each pinned JVM cell as a separate test, downloading one JDK at a time. Native tests are explicitly skipped in ordinary solution test runs unless `TARGET_RID` is set; CI's tests need no `TARGET_RID`, as they run on the runtime that CI published the samples for. `dotnet build src/JvmBridge.Build -c Release -t:CompileFixtures` compiles portable Java 8 fixture bytecode once into `artifacts/fixtures`; without it, as in CI, each cell compiles the fixtures with its own JDK. Coverage includes startup, late attachment, native exports, ABI comparisons, JNI calls, Unicode, references, native threads, transformation/retransformation, failure containment, and shutdown.

For a focused run with an installed JDK:

```sh
dotnet publish samples/HelloAgent -c Release -r linux-arm64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -o artifacts/agent/linux-arm64
dotnet publish samples/JavaHost -c Release -r linux-arm64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -o artifacts/host/linux-arm64
TARGET_RID=linux-arm64 JVM_JDK_HOME=/path/to/jdk JVM_JAVA_MAJOR=25 dotnet test tests/JvmBridge.IntegrationTests -c Release
```

Set `NATIVE_COMPILER` when the C compiler is not `cc` (`cl` on Windows). Logs, native/managed ABI reports, and per-cell results are written to `artifacts/results`. `dotnet build src/JvmBridge.Build -c Release -t:VerifyCoverage` requires all available manifest cells under `artifacts/results` to have passed; it is for results gathered from every target, which CI doesn't gather: each of its test jobs fails on its own cells. Do not treat a subset run as full coverage.

## Footprint

`FootprintTests` in `tests/JvmBridge.IntegrationTests` measures a minimal agent (`samples/FootprintAgent`) and is skipped unless `JVMBRIDGE_FOOTPRINT=1` and `TARGET_RID` are set. Publish the sample next to it, then run the test; set `JVMBRIDGE_JDK_<major>` to use an installed JDK instead of downloading the pinned one:

```sh
dotnet publish samples/FootprintAgent -c Release -r linux-arm64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -o artifacts/footprint-agent/linux-arm64
TARGET_RID=linux-arm64 JVMBRIDGE_FOOTPRINT=1 dotnet test tests/JvmBridge.IntegrationTests -c Release --filter FullyQualifiedName~FootprintTests
```

Results go to `artifacts/footprint/<rid>/hotspot-<major>.json`. CI's tests do the same on every runtime that runs on a runner, without the variable, and report the figures in the job summary without checking them against limits. To refresh the published table, gather the `artifacts/footprint` directories of a run on each target and run `dotnet run --project src/JvmBridge.Maintenance -- footprint <directory>`.

## Java 6 and 7

`LegacyJavaTests` runs `samples/LegacyAgent` on Zulu 6 and 7, from the legacy list of `src/JvmBridge.Build/PinnedJdks.g.cs`, and on Temurin 8 and 21. Zulu 6 and 7 exist for x64 only, so it runs on `linux-x64` only. Publish the sample against the glibc 2.12 sysroot of the `glibc-sysroot` action, then run it; with `JVMBRIDGE_LEGACY_GLIBC_IMAGE` set, it also checks the glibc floor and runs Java 6 and 7 in that image:

```sh
dotnet publish samples/LegacyAgent -c Release -r linux-x64 --self-contained -p:PublishAot=true -p:JvmBridgeVersion="$package_version" -p:SysRoot=/crossrootfs/x64-glibc-2.12 -p:LinkerFlavor=lld -o artifacts/legacy-agent/linux-x64
TARGET_RID=linux-x64 JVMBRIDGE_LEGACY_GLIBC_IMAGE=centos:6 dotnet test tests/JvmBridge.IntegrationTests -c Release --filter FullyQualifiedName~LegacyJavaTests
```

CI's linux-x64 tests do this on every commit, and publishing waits for them: they build the sysroot from the CentOS 6 packages themselves, as `LegacySysroot` in the tests does, publish the sample against it and name the image. The maintenance tool's `resolve` and `update` refresh the legacy inventory with the main one.

## Reproducible maintenance

```sh
dotnet build src/JvmBridge.Build -c Release -t:Generate
dotnet build src/JvmBridge.Build -c Release -t:VerifyGenerated
```

Generation verifies header checksums and uses a fixed Clang target and shim headers to avoid host-dependent declarations. The shims cover only unused stdio declarations and Clang's target-specific va_list. Upstream headers are unmodified. Native C probes independently validate the actual target ABI, including capability bits and callback/function-table offsets.

The maintenance tool discovers stable releases, pins archive checksums and regenerates bindings when it is run; nothing runs it on a schedule. Renovate maintains the SDK, packages and tools. Compatible updates may merge after the checks pass. Public API changes and new platforms require review.

Every validated commit to `main` publishes its tested NuGet package using Trusted Publishing. Versions follow [**NetAgents' timestamp scheme**](https://github.com/caunt/NetAgents): `YY.M.D.B`, with a UTC time-of-day bucket from 1000 through 9999. CI works the version out once and uses it throughout packaging, testing, and publishing. There are no release PRs, tags or GitHub releases; the publisher never rebuilds the package. Each package carries the subject of the commit it was packed from as its release notes.

The samples belong to the solution and inherit NetAgents and central package management. They use package references, never project references. Ordinary restores use the published JvmBridge baseline in `Directory.Packages.props`; the local feed is added only when `artifacts/packages` exists. CI packs first, and the samples take the run's version when `artifacts/packages` has the package of that version, so they test the freshly packed artifact. Renovate ignores JvmBridge itself to prevent publishing a package from triggering another sample dependency update and publication; update the published baseline deliberately when the examples require newer APIs. Set `<JvmBridgeAbiInspector>true</JvmBridgeAbiInspector>` to generate the ABI inspector during compilation from the referenced package's native types; no inspector source is checked in.

See also [**automation setup**](automation.md) and [**agent instructions**](../AGENTS.md). Original code is [**MIT licensed**](../LICENSE); see [**upstream notices**](../NOTICE.md) for header provenance.

# Automation

CI is `.github/workflows/ci.yml`, which hands the run to the shared workflow of [**void-community/Actions**](https://github.com/void-community/Actions): it gives the run a version, packs the packages, publishes the samples for every runtime, runs the tests once for every runtime, pushes the packages from `main`, and runs Renovate every hour. All available JVM matrix cells are required. Tests are not skipped based on Conventional Commit types.

`src/JvmBridge.Build/JvmBridge.Build.csproj` owns the generation and verification targets and supplies MSBuild tasks for pinned-header generation, native ABI verification, Java fixture compilation, and coverage validation. `src/JvmBridge.Generator` emits agent entry points and the opted-in ABI inspector directly into consumer compilations. `src/JvmBridge.Maintenance` only refreshes vendor inputs. Tests live in the xUnit projects under `tests`; real JVM cells are theories in `JvmBridge.IntegrationTests`, not commands in a custom test runner. Samples belong to the solution, inherit NetAgents and central package management, and remain independent NuGet consumers.

## What a run does

The packing comes first and hands its packages to every later job in `artifacts/packages`. `Directory.Packages.props` takes the run's version for the samples when the package of that version is there, so the samples and the tests use the packages of the run, never a project reference and never the published baseline.

The build publishes `samples/HelloAgent`, `samples/JavaHost` and `samples/FootprintAgent` for every runtime of the shared workflow, the `linux-x64` and `linux-arm64` ones against the Ubuntu 18.04 sysroot of .NET's cross-build image, and hands them to the tests in `artifacts/publish-<runtime>`. `release: false` in `ci.yml` keeps them out of a GitHub release.

The tests then run once for every runtime: on the runner of the runtime, as x86 on Windows for `win-x86`, in an Alpine container for `linux-musl-x64` and `linux-musl-arm64`, and in an ARM32 container on an arm64 runner, whose CPU runs 32-bit ARM code without emulation, for `linux-arm`. Building `tests/JvmBridge.IntegrationTests` copies the samples of the runtime it builds on to where a developer publishes them, and the tests take that runtime as their target, so CI sets no `TARGET_RID`. On Windows the tests bring the environment of the Visual C++ compiler into their own process. Each JVM cell is a theory row that records its exact pinned archive and outcome and fails when the cell does.

What the shared workflow has no runtime for is a test of its own, in CI's `linux-x64` tests unless noted:

- `GlibcFloorTests` holds the glibc floor of the `linux-x64` and `linux-arm64` agents and runs them in CentOS 7.
- `LegacyJavaTests` builds the glibc 2.12 sysroot, publishes `samples/LegacyAgent` against it and runs it on Java 6, 7, 8 and 21, on Java 6 and 7 also in CentOS 6.
- `MuslArmTests` cross-builds HelloAgent for `linux-musl-arm` in the image of .NET's cross-build toolchain and checks its exports.
- `GeneratedCodeTests` generates the native declarations and the compatibility page again and compares them with the checked-in ones.
- `FootprintTests` measures a minimal agent on every runtime that runs on a runner and writes the figures to the job summary. None of them fails a run.

## Repository settings

Use `main` and enable automatic branch deletion. When `main` is protected, require the `entrypoint / required` check. Renovate needs the `RENOVATE_TOKEN` secret of the organization.

`ci.yml` asks the shared workflow for two more apt packages on the Linux runners: `mingw-w64-i686-dev`, which packing the Windows x86 compatibility DLL needs, and `rpm2cpio`, which unpacks the glibc 2.12 sysroot.

## Packages and releases

Every push to `main` packs, builds and tests, including the complete available native JVM matrix, and then the `nuget` job of `ci.yml` pushes the packages of that run. Pull requests run the same validation but cannot publish. The packages, the published samples and their archives remain downloadable as artifacts of the run. There are no release PRs, tags or GitHub releases. Each package's `releaseNotes` carry the subject of the commit it was packed from, which `Directory.Build.targets` reads from git.

Versioning matches [**NetAgents**](https://github.com/caunt/NetAgents): `YY.M.D.B`, where `B = 1000 + floor(secondsSinceUtcMidnight * 9000 / 86400)`. For example, noon UTC on 2026-09-22 produces `26.9.22.5500`. The shared workflow works the version out once, and every job of the run uses it. As in NetAgents, runs within the same 9.6-second bucket have the same version; NuGet skips duplicates rather than replacing existing packages.

The trusted-publishing policy on nuget.org names the repository `void-community/JvmBridge`, the workflow `ci.yml` and the NuGet user `caunt`, with no environment, and must cover every package: `JvmBridge`, `JvmBridge.Testing`, `JvmBridge.Templates`, `JvmBridge.Logging` and `JvmBridge.Tool`. `NuGet/login` exchanges GitHub's OIDC identity for a short-lived credential using `id-token: write`; no stored `NUGET_API_KEY` secret is needed. The job downloads the tested packages and never rebuilds them.

## Updating coverage

`src/JvmBridge.Build/Models/Compatibility.cs` lists the targets and the implementations of the JVM grid. `src/JvmBridge.Build/PinnedJdks.g.cs`, which `dotnet run --project src/JvmBridge.Maintenance -- update` writes, pins each available vendor archive with SHA256 or records an explicit unavailable cell, from Java 8 to the newest release, and the Zulu Java 6 and 7 archives that only tests use. The updater refuses to silently drop previously available combinations. Network failures are errors, not evidence of unsupported platforms. Nothing runs the updater on a schedule: run it when a new Java release is to be covered, and commit what it writes.

HotSpot uses Temurin with Zulu fallback. OpenJ9 uses IBM Semeru's public release archives. Native builds, ABI probes, JVM host tests, startup agents, and late attachment run against each available combination.

Android ART, Apple mobile targets, and missing Java distributions remain explicitly experimental/unavailable until matching runtime fixtures exist. They are never reported as runtime verified merely because source code compiles.

# Automation

The reusable workflows follow the same organization as Void: runnable PR/main workflows orchestrate versioning, builds, tests, and publication. All available JVM matrix cells are required. Tests are not skipped based on Conventional Commit types.

`src/JvmBridge.Build/JvmBridge.Build.csproj` owns the generation and verification targets and supplies MSBuild tasks for pinned-header generation, native ABI verification, Java fixture compilation, and coverage validation. `src/JvmBridge.Generator` emits agent entry points and the opted-in ABI inspector directly into consumer compilations. `src/JvmBridge.Maintenance` only refreshes vendor inputs. Tests live in the xUnit projects under `tests`; real JVM cells are theories in `JvmBridge.IntegrationTests`, not commands in a custom test runner. Samples belong to the solution, inherit NetAgents and central package management, and remain independent NuGet consumers. CI packs the library before restoring the full solution.

The native workflow first publishes each sample directly with `dotnet publish`, then runs xUnit with `TARGET_RID` set. ARM32 uses the same tests published as a self-contained xUnit executable. Each JVM cell records its exact pinned archive and outcome; aggregate coverage rejects missing cells, failed cells, mismatched archive provenance, or missing native export verification.

## Repository settings

Use `main`, enable auto-merge and automatic branch deletion, and protect main with the `required` PR check. Enable Actions to create pull requests. Bot updates use explicit workflow dispatches because pushes/PRs created by GITHUB_TOKEN do not normally trigger other workflows. A reconciliation schedule validates main after bot merges if no commit run exists.

Set the repository variable `AUTOMATION_PR_ENABLED=true` after allowing Actions-created PRs at organization level, or after configuring a dedicated `AUTOMATION_TOKEN` with repository permissions. The organization currently blocks PR creation by its default Actions token. PR-producing jobs are gated until that prerequisite is resolved; JDK maintenance still uploads generated updates as artifacts. No personal token is copied automatically.

Renovate uses `RENOVATE_TOKEN` when configured, then `AUTOMATION_TOKEN`, then the repository token. An organization-installed Renovate app is also compatible. The fallback does not require sharing a personal token with pull-request workflows.

## Packages and releases

Every push to `main` builds and tests the package, including the complete available native JVM matrix, then calls `publish-nuget.yml`. Pull requests run validation but cannot publish. NuGet packages and native examples remain downloadable as CI artifacts. No release PR, tag, GitHub release, or automation-enable variable is required for NuGet publishing.

Versioning matches [**NetAgents**](https://github.com/caunt/NetAgents): `YY.M.D.B`, where `B = 1000 + floor(secondsSinceUtcMidnight * 9000 / 86400)`. For example, noon UTC on 2026-09-22 produces `26.9.22.5500`. The version job freezes one UTC timestamp; all builds and tests use that version. Re-running version calculation creates a new time-based version. Retrying only publication reuses the already validated artifact. As in NetAgents, builds within the same 9.6-second bucket have the same version; NuGet skips duplicates rather than replacing existing packages.

The existing trust identity is preserved: repository `void-community/JvmBridge`, workflow `publish-nuget.yml`, environment `nuget`, NuGet user `caunt`. `NuGet/login` exchanges GitHub OIDC identity for a short-lived credential using `id-token: write`; no stored `NUGET_API_KEY` secret is needed. The publishing job downloads the tested package and pushes that exact version and its associated symbols. It does not rebuild. Use `Commit Workflow` on `main` for a manual validation-and-publish retry.

## Updating coverage

`src/JvmBridge.Build/compatibility.json` describes Java majors and target capabilities; `src/JvmBridge.Build/jdks.lock.json` pins each available vendor archive with SHA256 or records an explicit unavailable cell. The updater refuses to silently drop previously available combinations. Network failures are errors, not evidence of unsupported platforms.

HotSpot uses Temurin with Zulu fallback. OpenJ9 uses IBM Semeru's public release archives. Native builds, ABI probes, JVM host tests, startup agents, and late attachment run against each available combination. Runtime tests are executed inside matching Alpine containers for musl and with a cross toolchain/emulation where needed for ARM32.

Android ART, Apple mobile targets, and missing Java distributions remain explicitly experimental/unavailable until matching runtime fixtures exist. They are never reported as runtime verified merely because source code compiles.

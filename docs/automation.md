# Automation

The reusable workflows follow the same organization as Void: runnable PR/main workflows orchestrate versioning, builds, tests, and publication. All available JVM matrix cells are required. Tests are not skipped based on Conventional Commit types.

## Repository settings

Use `main`, enable auto-merge and automatic branch deletion, and protect main with the `required` PR check. Enable Actions to create pull requests. Bot updates use explicit workflow dispatches because pushes/PRs created by GITHUB_TOKEN do not normally trigger other workflows. A reconciliation schedule validates main after bot merges if no commit run exists.

Set the repository variable `AUTOMATION_PR_ENABLED=true` after allowing Actions-created PRs at organization level, or after configuring a dedicated `AUTOMATION_TOKEN` with repository permissions. The organization currently blocks PR creation by its default Actions token. PR-producing jobs are gated until that prerequisite is resolved; JDK maintenance still uploads generated updates as artifacts. No personal token is copied automatically.

Renovate uses `RENOVATE_TOKEN` when configured, then `AUTOMATION_TOKEN`, then the repository token. An organization-installed Renovate app is also compatible. The fallback does not require sharing a personal token with pull-request workflows.

## Packages and releases

Each validation run packs a unique CI version and tests standalone consumers against that artifact. NuGet packages and native example binaries are downloadable as CI artifacts. release-please opens release PRs after successful main validation; merging a release PR creates a release and uploads tested example archives.

NuGet publication is deliberately a separate manual workflow for the initial rollout. Create an environment named `nuget`, configure `NUGET_API_KEY` for the package owner, and dispatch the NuGet workflow with an existing version tag. It rebuilds and runs the native matrix for that tag before pushing the exact resulting package. Trusted publishing can replace the API key once the NuGet owner configures its trust policy. Never put credentials in tracked files.

## Updating coverage

`eng/compatibility.json` describes Java majors and target capabilities; `eng/jdks.lock.json` pins each available vendor archive with SHA256 or records an explicit unavailable cell. The updater refuses to silently drop previously available combinations. Network failures are errors, not evidence of unsupported platforms.

HotSpot uses Temurin with Zulu fallback. OpenJ9 uses IBM Semeru's public release archives. Native builds, ABI probes, JVM host tests, startup agents, and late attachment run against each available combination. Runtime tests are executed inside matching Alpine containers for musl and with a cross toolchain/emulation where needed for ARM32.

Android ART, Apple mobile targets, and missing Java distributions remain explicitly experimental/unavailable until matching runtime fixtures exist. They are never reported as runtime verified merely because source code compiles.

# Repository guidelines

JvmBridge is a generic JNI/JVMTI library and NativeAOT agent toolkit. Keep all production APIs, examples, documentation, and automation independent of any particular Java application.

## Layout

- `src/JvmBridge`: runtime ownership helpers, agent lifecycle, generated native declarations, and NuGet build integration.
- `src/JvmBridge.Generator`: incremental C# generator shipped inside the same JvmBridge NuGet package.
- `eng`: pinned JDK inventories, unmodified upstream headers, deterministic generation, native tests, and reporting.
- `tests`: xUnit tests, generic Java fixtures, and the generated ABI inspector.
- `samples`: standalone consumers restored from the packed NuGet artifact, never project references.
- `.github`: reusable CI, release-please, dependency maintenance, and repository templates.

## Source conventions

Use four-space C# indentation, descriptive identifiers, underscore-prefixed private fields, and camelCase parameters. Put braces on their own lines and omit braces for single-line statement bodies. Keep method signatures on a single line. Prefer pattern matching and nameof. Async methods use an Async suffix. Never silence nullable warnings with the null-forgiving operator. Preserve unrelated whitespace.

Generated files preserve upstream names and generator formatting; these style rules apply to handwritten code. Never edit `*.g.cs` manually. Change the generator or pinned headers, regenerate, and review the complete diff. Keep original upstream license notices and SHA256 provenance.

## Native correctness

- JNI environments and local references belong to a thread and a scope. Never retain callback locals or use an environment from another thread.
- Global references need explicit disposal while their JVM remains alive. Only detach threads attached by the current ownership scope.
- Use UTF-16 for Java/.NET string values and modified UTF-8 for JNI names/options. Preserve null code units and surrogate code units.
- Use argument-array (`*A`) JNI functions. Raw variadic and va_list slots are addresses, not portable managed call signatures.
- Compare ABI layouts to real C headers; do not infer correctness from a successful managed build.
- Request only needed capabilities and check every JNI/JVMTI return value. Do not call version-specific slots merely because newer headers contain them.
- Keep every unmanaged entry point and callback non-throwing. Never unload a NativeAOT agent with FreeLibrary/dlclose. OnUnload performs logical cleanup during JVM shutdown.
- Keep Java agent operations within their allowed JVMTI phase. Never call ordinary JNI Java APIs from Agent_OnLoad.

## Verification

Install the SDK pinned in global.json and restore the pinned tool with `dotnet tool restore`. Run `python eng/check.py` after source, generator, manifest, or build changes. It verifies deterministic generation, lock coverage, documentation, build, and xUnit tests.

For native changes, pack a fresh package version, then run `python eng/test_agent.py --rid <RID> --version <VERSION>` with the required native compiler. This publishes consumers, checks exports and ABI layouts, and runs real JVM startup/attach/transform/Unicode/thread/reference/failure/shutdown tests. Do not substitute project-reference tests for package-consumer tests. Record which platforms and Java versions actually executed.

CI must account for every configured matrix cell. Missing or timed-out available cells are failures; unavailable cells require a specific reason. Never weaken tests, change a passing cell to unavailable, or skip source/build/workflow changes to get green checks. Long native builds and multi-JVM suites may take several minutes: wait for completion.

Regenerate declarations with `python eng/generate.py`, the ABI inspector with `python eng/abi.py`, and inventory documentation with `python eng/report.py`. `python eng/update.py` deliberately changes pinned inputs and may download large JDKs. Never replace a checksum failure with an unchecked download.

## Commits and releases

Use Conventional Commits with a scope, a gitmoji after the scope, and past-tense subjects for completed work. Allowed types: feat, fix, perf, deps, revert, docs, style, chore, refactor, test, build, ci. Use feat/fix for production source behavior. Include a commit body describing observable behavior changes, including "none" when appropriate.

Examples: `feat(agents): ✨ added generated native entry points`, `ci(test): ✅ expanded JVM compatibility checks`.

Pull request descriptions use the repository template. release-please owns CHANGELOG.md, versions, release PRs, and tags: never edit CHANGELOG.md manually. Do not publish NuGet packages without a verified release and configured publishing credentials. Never print tokens or copy organization secrets into this repository.

## Documentation

Keep README examples executable and aligned with the package API. Bold documentation link captions and do not put inline code inside link captions. Generated compatibility tables describe inventory; only the corresponding CI report establishes runtime support. Distinguish native execution, emulation, experimental targets, and unavailable JVM loaders.

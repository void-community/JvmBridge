# Repository guidelines

JvmBridge is a generic JNI/JVMTI library and NativeAOT agent toolkit. Keep all production APIs, examples, documentation, and automation independent of any particular Java application.

## Layout

- `src/JvmBridge`: runtime ownership helpers, agent lifecycle, generated native declarations, and NuGet build integration.
- `src/JvmBridge.Generator`: incremental C# generator shipped inside the same JvmBridge NuGet package.
- `src/JvmBridge.Build`: MSBuild tasks for deterministic native declarations, ABI inspection, fixture compilation, and coverage validation; owns pinned inventories and unmodified headers.
- `src/JvmBridge.Maintenance`: deliberate vendor-inventory and header updates, separate from normal builds.
- `tests`: xUnit unit and native integration tests and generic Java fixtures. The package's Roslyn generator emits the ABI inspector into the opted-in sample compilation.
- `samples`: standalone consumers restored from the packed NuGet artifact, never project references.
- `.github`: reusable CI, NuGet Trusted Publishing, dependency maintenance, and repository templates.

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

Install the .NET 11 RC1 SDK and .NET 10 runtime, then run `dotnet tool restore --tool-manifest src/JvmBridge.Build/.config/dotnet-tools.json` and `dotnet build src/JvmBridge.Build -c Release -t:VerifyGenerated -p:RestoreLockedMode=true`. Pack a fresh version with `dotnet pack src/JvmBridge -c Release -o artifacts/packages -p:Version=<VERSION>` before restoring the full solution with `dotnet restore JvmBridge.slnx --locked-mode -p:JvmBridgeVersion=<VERSION>`. Run `dotnet build JvmBridge.slnx -c Release --no-restore -p:JvmBridgeVersion=<VERSION>`, then `dotnet test JvmBridge.slnx -c Release --no-build --no-restore -p:JvmBridgeVersion=<VERSION>`. Both samples belong to the solution and inherit central package management and NetAgents. Sample restore locks live under obj per package version and RID; source and test dependency locks remain checked in.

For native changes, pack a fresh package version, then publish both samples with `dotnet publish samples/<PROJECT> -c Release -r <RID> --self-contained -p:PublishAot=true -p:JvmBridgeVersion=<VERSION> -o <OUTPUT>`. Use `artifacts/agent/<RID>` for HelloAgent and `artifacts/host/<RID>` for JavaHost. Cross-compilation passes `SysRoot`, `LinkerFlavor`, and `ObjCopyName` directly to each publish command. Set `TARGET_RID` and run `dotnet test tests/JvmBridge.IntegrationTests -c Release` with the required native compiler (`NATIVE_COMPILER` overrides the default). This checks exports and ABI layouts and runs real JVM startup/attach/transform/Unicode/thread/reference/failure/shutdown tests. `JVM_JDK_HOME` and `JVM_JAVA_MAJOR` select an installed JDK for a focused run. Do not substitute project-reference tests for package-consumer tests. Record which platforms and Java versions actually executed.

CI must account for every configured matrix cell. Missing or timed-out available cells are failures; unavailable cells require a specific reason. Never weaken tests, change a passing cell to unavailable, or skip source/build/workflow changes to get green checks. Long native builds and multi-JVM suites may take several minutes: wait for completion.

Regenerate native declarations and inventory documentation with `dotnet build src/JvmBridge.Build -c Release -t:Generate -p:RestoreLockedMode=true`. The Roslyn generator creates ABI-inspector code at compile time when `JvmBridgeAbiInspector` is enabled; never check that generated output into source control. `dotnet run --project src/JvmBridge.Maintenance -- update` deliberately changes pinned inputs and may download large JDKs; run generation afterward. Never replace a checksum failure with an unchecked download.

## Commits and releases

Use Conventional Commits with a scope, a gitmoji after the scope, and past-tense subjects for completed work. Allowed types: feat, fix, perf, deps, revert, docs, style, chore, refactor, test, build, ci. Use feat/fix for production source behavior. Include a commit body describing observable behavior changes, including "none" when appropriate.

Examples: `feat(agents): ✨ added generated native entry points`, `ci(test): ✅ expanded JVM compatibility checks`.

Pull request descriptions use the repository template. Every validated main commit publishes its tested NuGet artifact through `publish-nuget.yml` and the `nuget` environment using Trusted Publishing. There are no release PRs, release tags, or release-please workflows. Versions follow NetAgents' UTC `YY.M.D.B` scheme, with the timestamp frozen once per workflow run; `B` is 1000 plus the integer part of seconds-since-midnight multiplied by 9000/86400. Never rebuild packages in the publishing job, publish unvalidated artifacts, print tokens, or copy organization secrets into this repository.

## Documentation

Keep README examples executable and aligned with the package API. Bold documentation link captions and do not put inline code inside link captions. Generated compatibility tables describe inventory; only the corresponding CI report establishes runtime support. Distinguish native execution, emulation, experimental targets, and unavailable JVM loaders.

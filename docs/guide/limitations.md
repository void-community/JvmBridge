# Limitations

- **Method bodies only on loaded classes.** The JVM forbids adding methods, fields or interfaces when a class is retransformed. Edits made when a class first loads are not limited this way, but a late attach can only change bodies of classes that are already loaded.
- **Matchers know what the agent has seen.** `ForSubclassesOf` follows the supertypes the agent recorded from class loads and from the classes that were already loaded when it started or when the patch was registered; an ancestor it never saw ends the walk. `ForAnnotatedWith` reads the class bytes, so on its own it cannot find already loaded classes.
- **Retransforming at run time needs the capability from the start.** `PatchNow` and `Unpatch` can only retransform loaded classes if `can_retransform_classes` was requested before events were enabled: automatic for an attached agent, `TryEnableRetransformation` in `Configure` otherwise.
- **Edits keep constant indices and code offsets.** `ClassFileEditor` appends constants and rewrites code at the same length. It does not insert or delete instructions and does not interpret stack map frames, so it is not a general bytecode editor.
- **One agent per tooling environment.** Every load of an agent library gets its own JVMTI environment. Two copies of the same agent in one JVM start two NativeAOT runtimes; the second skips helper classes that the first already defined.
- **A NativeAOT library is never unloaded.** `OnUnload` performs logical cleanup, and the library stays mapped until the process exits.
- **JNI is unavailable in `Agent_OnLoad`.** Do ordinary JNI work from `OnVmInit` or `OnAttach`. The class-file hook can run before JNI is permitted.
- **OpenJ9 differs from HotSpot.** Checked JNI rejects a null-loader class definition, so bootstrap helpers go through a jar on the bootstrap search path, and some builds return `JNI_ERR` from `DestroyJavaVM`. The CI reports record these baselines rather than hiding them.
- **Platforms.** The compatibility inventory lists what can be downloaded, and the CI report for a commit shows what actually passed. Mobile targets and platforms without a suitable JVM are experimental or unavailable.
- **Options are text.** `context.Options` is the raw string after `=` in `-agentpath`; `ParsedOptions` understands comma-separated `key=value` entries and flags, nothing else.
- **Native API is unsafe by design.** The generated JNI and JVMTI bindings are raw escape hatches. Variadic and `va_list` entries are exposed as addresses.

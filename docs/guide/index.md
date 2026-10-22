# User guide

JvmBridge writes JVM agents in C#: generated JNI and JVMTI declarations, explicit Java reference ownership and NativeAOT entry points in one NuGet package. Start with the [**README**](../../README.md) to build and load a first agent, then read:

- [**Concepts**](concepts.md): lifecycle, class loaders and helper classes, references and ownership, threads, logging and options.
- [**Patching classes**](patching.md): the class-file model, `ClassPatch`, and helper classes written in Java or C#.
- [**Recipes**](recipes.md): short answers to the usual jobs.
- [**Loading and attaching**](attaching.md): `-agentpath`, `JAVA_TOOL_OPTIONS`, `jcmd` and the Attach API.
- [**Testing an agent**](testing.md): JvmBridge.Testing and the project template.
- [**Troubleshooting**](troubleshooting.md): symptoms and fixes at run time.
- [**Limitations**](limitations.md): what the JVM and the editor do not allow.
- [**Stability and versioning**](stability.md): version numbers, what is stable, the deprecation policy.
- [**Diagnostics**](../diagnostics.md): every `JVMB` build error.
- [**Compatibility**](../compatibility.md): the generated inventory of JVMs and platforms.
- [**Footprint**](../footprint.md): library size, start-up time and memory that an agent costs.

Working on JvmBridge itself? See [**contributing**](../contributing.md).

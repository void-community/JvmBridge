# Concepts

How an agent runs, which loader sees what, who owns a Java reference, and which thread may do what. Read this before the recipes.

## Lifecycle

The generator emits `Agent_OnLoad`, `Agent_OnAttach` and `Agent_OnUnload` for the single concrete `JavaAgent` subclass of the library. The JVM calls one of the first two, and the runtime then drives your overrides:

1. **`Configure`** runs first, before any event is installed. Request capabilities (`TryEnableRetransformation`), register patches (`Patch`) and declare helper classes (`DefineClass`) here. Those calls throw once events are enabled.
2. The runtime installs the callbacks for VM initialization, VM death, class preparation and class-file loading.
3. **`OnLoad`** runs for `-agentpath` and `JAVA_TOOL_OPTIONS`. **`OnAttach`** runs when the agent is loaded into a running JVM and defaults to calling `OnLoad`; callbacks are already active, so it can retransform classes that are already loaded.
4. **`OnVmInit`** gives you a `JavaEnvironment` once the VM is live. On a late attach the VM initialization event has already happened, so do start-up work in `OnAttach` too.
5. **`OnClassPrepare`** and **`TransformClass`** run as classes are prepared and defined. Patches registered with `Patch` apply when classes load, and can be added or removed while the JVM runs.
6. **`OnCommand`** runs when the library is loaded into the JVM again, which is how a running agent is told what to do (see [**Loading and attaching**](attaching.md#telling-a-running-agent-what-to-do)).
7. **`OnVmDeath`** and **`OnUnload`** perform logical cleanup exactly once. A NativeAOT library is never unloaded with `FreeLibrary`/`dlclose`; it stays mapped until the process exits.

Any exception that escapes one of your overrides is reported and contained; it never crosses into the JVM. A failure in `Configure`, `OnLoad` or `OnAttach` makes the entry point return `JNI_ERR`.

## Class loaders and helper classes

A patched class often needs a helper class to call. `ClassLoaderTarget` decides where the helper is defined:

- **`Bootstrap`** defines it at VM initialization (or at attach) so every loader sees it. HotSpot defines it directly. OpenJ9's checked JNI treats defining a class with a null loader as fatal, so there the helpers go into a temporary jar on the bootstrap search path instead.
- **`PatchedClassLoader`** defines it once in each loader that defines a patched class, before that loader's first patched class, for helpers that must see application classes. It cannot serve classes of the bootstrap loader, which the log reports.

Appending to the bootstrap search path makes HotSpot print a class-data-sharing notice, which is why HotSpot uses the direct route.

## References and ownership

Agent callbacks borrow their JNI environment, and every Java reference belongs to a thread and a scope:

- A **local** reference (`JavaLocalReference`) is valid only in the callback that produced it. Dispose it before the callback returns. `GetSuperclass`, `GetObjectClass` and `AgentContext.GetClassLoader` return disposable locals; a bootstrap loader, a root superclass or a null field has an empty reference (`Handle == 0`).
- Promote a local to a **global** (`environment.NewLocalReference(handle).ToGlobal()`) before retaining it or passing it to another attached thread, and dispose the global while its JVM is alive.
- A **weak global** (`JavaWeakGlobalReference`) observes an object without keeping it alive. On any attached thread of the same JVM, `using JavaLocalReference? current = weak.TryPromote(environment)` yields null when the referent was collected. Use the promoted local for the whole operation; checking for collection and then using the weak handle is racy.
- Method and field IDs do not retain their declaring class or loader. Never cache an ID beyond that class's lifetime, and keep classes from independent loaders distinct even when names and descriptors match.
- `IsSameObject` compares Java identity, not handle values or class names; two null handles compare equal.

Java exceptions raised by a call you made surface as `JavaException`, which captures `JavaTypeName`, `JavaMessage` and `JavaStackTrace` (at most eight throwables and 64 frames per throwable) and clears the pending exception. JNI failures preserve their result in `JniException.ErrorCode`.

## Threads and callbacks

- A callback runs on the Java thread that triggered it, with that thread's JNI environment. Do not share the environment, class bytes or callback-local references across threads, and expect callbacks to be reentrant: avoid shared callback-local state.
- Attachments only detach threads that they attached.
- Never call ordinary JNI from `Agent_OnLoad` (that is, from `Configure` or `OnLoad` before the VM is live). The class-file hook can run before JNI is permitted: check `callback.CanUseJni` before touching `callback.Environment`, which can be null in the primordial phase.
- In `TransformClass`, `Loader`, `ProtectionDomain` and `ClassBeingRedefined` are zero for a bootstrap loader, a missing protection domain and an initial definition respectively, and `ClassFile.Name` can be null. A pending Java exception is left untouched rather than invoking the transformer.
- On Unix HotSpot, preload the JDK's `libjsig` when the JVM and the agent's runtime share a process, so that signal handlers chain. Do not inject HotSpot's library into OpenJ9.

## Logging and options

`context.Log("text")` writes an information line prefixed with the agent's name to standard error. It is safe in every callback, needs no ICU and never throws. `context.Log(AgentLogLevel.Debug, "text")` filters by `context.LogLevel`, which starts as the `log` option (`-agentpath:/path/agent.so=log=debug`; `trace`, `debug`, `info`, `warn`, `error` or `none`).

`context.Options` is the raw text after `=`; `context.ParsedOptions` reads it as comma-separated entries (`log=debug,trace-classes,limit=5`) with `TryGetValue`, `IsEnabled` for flags and `TryGetInt32`. Replace `AgentLog.Sink` to send every line somewhere else, or add the [**JvmBridge.Logging**](https://www.nuget.org/packages/JvmBridge.Logging) package for an `ILogger` that writes to the agent log and a redirect of the agent log into an `ILogger`.

The runtime writes to the file descriptor directly instead of using `Console`, because `Console` initializes terminal and globalization state, which aborts the process on a host without libicu.

## Hosting a JVM from .NET

The same package also creates a JVM inside a .NET process: [**JavaHost**](../../samples/JavaHost) creates one from an explicit native-library path, invokes Java and handles exceptions. Set `<JvmBridgeHost>true</JvmBridgeHost>` for NativeAOT executables that embed a JVM: on Intel macOS this reserves a 1 MiB null guard instead of the low 4 GiB so OpenJ9 keeps room for its compressed-reference metadata, and on Windows AMD64 it opts the host executable out of CET shadow-stack compatibility because HotSpot CPU feature probes are incompatible with it (an explicit `CETCompat` wins). On Windows x86, `JavaVirtualMachine.Create` installs the packaged compatibility handler for HotSpot's legacy `SafeFetch` probe. Dispose JVM-owned resources before destroying a hosted JVM; some OpenJ9 builds return `JNI_ERR` from `DestroyJavaVM` even in a plain C host.

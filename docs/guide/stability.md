# Stability and versioning

## Versions

Every validated commit to `main` publishes its packages (JvmBridge, JvmBridge.Testing, JvmBridge.Templates, JvmBridge.Logging and JvmBridge.Tool) at one shared timestamp version `YY.M.D.B`, for example `26.10.22.2272`: the UTC date and a time-of-day bucket from 1000 through 9999. A later version is newer; the numbers say nothing about the size of a change. Pin the version you tested against.

There are no tags or GitHub releases. Each package's `releaseNotes` list the commits that went into that version, and the repository history is the change log. Commit subjects follow Conventional Commits, so `feat` and `fix` mark behavior changes and a body states the observable effect ("none" when there is none).

## What is stable

These are changed only with a deprecation period (below):

- the `JavaAgent` lifecycle, `AgentContext` (logging, options, patch registration, helper classes) and the `JvmBridge.Agents` types it uses;
- `JavaEnvironment`, the owned reference types and the exception types;
- the declarative `ClassPatch` API, `ClassPatchResult` and the read-only class-file model;
- the MSBuild properties and items the package documents (`JvmBridgeAgent`, `JavaSource`, `JavaRelease`, `JvmBridgeJdkHome`, `JvmBridgeProvisionJdk`, `JvmBridgeJdkMajor`, `JvmBridgeHost`);
- the `JVMB` diagnostic codes and their meaning.

## What is experimental

These can change or disappear in any version, with the change noted in the release notes:

- features marked experimental in their documentation while they settle; class matchers (`ForPackage`, `ForSubclassesOf`, `ForAnnotatedWith`, `ForMatching`), run-time patching (`PatchNow`, `Unpatch`, `PatchReport`), `OnCommand`, the event layer (`AgentEvents`, `JavaEvents`, `JavaCapability` and the `On...` event methods), the thread, stack and redefinition wrappers, the array, exception, monitor and local-frame helpers, and typed proxies from `JavaReference` are new and belong here until they have been in use;
- `JvmBridge.Native`, the generated raw JNI and JVMTI declarations, which follow the pinned OpenJDK headers and are regenerated when the pins change;
- anything reached through `RawHandle` or a raw function pointer;
- output formats meant for people, such as log wording (the default `name: message` line format is stable).

## Deprecation policy

A stable member is first marked `[Obsolete]` with a message that names its replacement, and stays for at least 90 days before removal. The commit that deprecates it says so, and its subject appears in the release notes of the version that ships it. A change that cannot keep the old behavior (for example one the JVM or an OpenJDK header forces) is announced the same way and called out as breaking in the commit body.

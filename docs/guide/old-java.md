# Java 6 and 7

One agent build runs on Java 6 and 7 as well as on Java 8 to 26. The runtime asks every JVM for the JNI and JVMTI versions that Java 6 knows, so what decides the oldest Java an agent runs on is the class files it brings: set `JavaRelease` to that Java.

```xml
<PropertyGroup>
  <JavaRelease>6</JavaRelease>
  <!-- Only for JavaSource items: downloads the pinned JDK 11, the last javac that compiles for Java 6. -->
  <JvmBridgeProvisionJdk>true</JvmBridgeProvisionJdk>
</PropertyGroup>
```

The [**LegacyAgent**](../../samples/LegacyAgent) sample is such an agent: a constant patch, a call redirected to a C# helper, `After` advice with a hook compiled from Java, event counts, and a thread of its own attached to the JVM.

## What JvmBridge negotiates

- **JNI 1.6.** `JNI_OnLoad` returns `JNI_VERSION_1_6`, and `AttachCurrentThread` and `JavaVirtualMachine.Create` ask for it. Java 8's JNI 1.8 added no functions, so nothing is lost on newer JVMs.
- **JVMTI 1.2, then 1.1, then 1.0.** The agent's tooling environment is the newest one the JVM accepts. Java 7 and late Java 6 builds offer 1.2; the oldest Java 6 builds offer 1.1. Every JVMTI function the runtime calls exists in 1.1, and retransformation, `IsModifiableClass` and adding to the bootstrap search path in the live phase need 1.1.
- **`context.JavaMajor`** is the JVM's Java major version, 6 for a `java.specification.version` of `1.6`. Use it to keep calls to JNI or JVMTI functions that a newer Java added (the module functions of Java 9, for example) away from older JVMs.

## Helper classes

A JVM refuses a class file newer than itself (`UnsupportedClassVersionError`, or `NoClassDefFoundError` when a patched class calls the helper), so every helper class must be no newer than the oldest Java the agent runs on:

- **`[JavaClass]` helpers** get the class file version of `JavaRelease`: 50 for Java 6, 51 for 7, 52 (Java 8) by default.
- **`JavaSource` items** are compiled with `javac --release $(JavaRelease)`. javac 9 to 11 compile for Java 6, and javac 12 to 19 stop at 7. With `JvmBridgeProvisionJdk`, `JavaRelease` 6 downloads the pinned Temurin 11 and uses it even when `JAVA_HOME` has a javac, and any other release downloads Temurin 17 (or the release itself, above 17). A `JvmBridgeJdkHome` you set is used as it is.
- **Classes you write with `ClassFileWriter`** take the version as `majorVersion`, which is 52 unless you pass it: `new ClassFileWriter("pkg/Helper", majorVersion: 50)`.

The patches themselves keep the version of the class they edit. Advice adds straight-line stubs without stack map frames, which the verifiers of Java 6 and later accept at any class version.

## The jar route

The `-javaagent` launcher in the jar is compiled for Java 6 and uses Java 6 APIs only, so `jvmbridge pack` and `JvmBridgeAgentJar` jars load on Java 6 and later.

## Linux: glibc 2.12

Many Java 6 systems are older than glibc 2.17, the floor of an agent published against .NET's sysroot: CentOS 6 has glibc 2.12, and the last `openjdk:6` image is Debian 7 with 2.13. For `linux-x64`, the `glibc-sysroot` action builds a glibc 2.12 sysroot from the last CentOS 6 packages:

```yaml
- id: sysroot
  uses: void-community/actions/glibc-sysroot@99cef201a5ae7a722304ecda04f6e94ce3426f66 # main
  with:
    rid: linux-x64
    glibc: '2.12'
- run: dotnet publish MyAgent.csproj -c Release -r linux-x64 --self-contained ${{ steps.sysroot.outputs.properties }}
```

The agent then asks for `GLIBC_2.12` at most. See [**Publishing for Linux**](publishing.md) for how it works and what it adds. `linux-arm64` has no glibc older than 2.17.

## Testing

`JvmBridge.Testing` has checksum-pinned Zulu builds of Java 6 and 7, Azul's last community builds: both for `linux-x64` and `win-x64`, and Java 7 also for `win-x86` and `osx-x64`. `[JdkData(6, 7)]` runs a test on them, and `CompileAsync(sources, release: 6)` compiles a fixture for Java 6 on a JDK from 6 to 11. Platforms without them skip the test with the reason.

## What CI runs

On every commit, CI publishes LegacyAgent for `linux-x64` against the glibc 2.12 sysroot, checks that it asks for no newer glibc, and runs it through `-agentpath` (with `-Xverify:all`) and through its jar:

- on Zulu 6, Zulu 7, Temurin 8 and Temurin 21 on Ubuntu 24.04;
- on Zulu 6 and Zulu 7 in a `centos:6` container (glibc 2.12).

The rest of the test matrix covers Java 8 to 26 on every platform.

## Not covered

- Java 6 and 7 run on `linux-x64` only in CI. The Windows and macOS builds of Zulu 6 and 7 are pinned for tests but not run.
- No Java 6 build offering only JVMTI 1.1 was available to test the fallback against: Zulu 6u119 offers 1.2.1.
- Late attach (`jcmd` or the Attach API) on Java 6 and 7, and OpenJ9 builds of them, are untested.
- The CentOS 6 runs use the runner's kernel, not CentOS 6's 2.6.32 kernel.

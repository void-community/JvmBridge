# Patching classes

`JvmBridge.ClassFiles` reads, edits and writes class files without a JVM, and `ClassPatch` applies such edits declaratively as classes load.

## Class files

`JavaClassFile.Read` exposes the version, flags, hierarchy, fields and methods, the typed constant pool and each `Code` attribute's instructions (offset, opcode, constant operand). It does not interpret stack maps, and it throws `ClassFormatException` for truncated input, trailing bytes, unknown constant tags and malformed code.

`ClassFileEditor` only makes edits that keep every existing constant index and code offset, so code, exception tables and stack map frames stay valid without being recomputed: new constants are appended once, a member reference can be retargeted everywhere, an invocation can be replaced in place, constants can be replaced, and access flags can be changed.

```csharp
using JvmBridge.ClassFiles;

JavaClassFile file = JavaClassFile.Read(bytes);
foreach (ClassMember method in file.Methods)
    Console.WriteLine($"{file.Name}.{method.Name}{method.Descriptor}");

ClassFileEditor editor = new(bytes);
int redirected = editor.ReplaceInvocation(
    JavaMethod.Static("java/util/Collections", "sort", "(Ljava/util/List;Ljava/util/Comparator;)V"),
    toOwner: "legacyforgepatcher/Collections");
byte[] patched = editor.ToArray();
```

`ReplaceInvocation` turns `invokestatic`, `invokevirtual` or `invokeinterface` into `invokestatic` of another owner at the same length; a virtual or interface receiver becomes the first parameter, and `invokeinterface` padding becomes `nop`. `ClassFileWriter` writes small classes made of static methods (including `native` ones for `RegisterNative`) with a deduplicating constant pool; straight-line code needs no stack map frames. `ModifiedUtf8.GetBytes` encodes class-file strings without the JNI terminator.

## ClassPatch

`ClassPatch.For(className)` describes the edits to one class. It is immutable and reusable. `.Method(name, descriptor)` scopes the edits that follow to one method (`.AnyMethod()` ends the scope); `.RedirectInvocation`, `.ReplaceConstant`, `.SetAccess`/`.SetFieldAccess` and the `.Edit(editor => ...)` escape hatch add edits. `patch.Apply(bytes)` returns a `ClassPatchResult` that says what changed, or why nothing did, so a test can assert on it without a JVM. A patch is all or nothing: when its method or invocation is missing, the class is left unchanged and the result names the reason.

```csharp
public sealed class PatcherAgent : JavaAgent
{
    internal static readonly ClassPatch TweakSort = ClassPatch.For("cpw/mods/fml/relauncher/CoreModManager")
        .Method("sortTweakList", "()V")
        .RedirectInvocation(JavaMethod.Static("java/util/Collections", "sort", "(Ljava/util/List;Ljava/util/Comparator;)V"), toOwner: "legacyforgepatcher/Collections");

    public override void Configure(AgentContext context)
    {
        byte[] helperBytes = File.ReadAllBytes("Collections.class"); // or build one with ClassFileWriter
        context.DefineClass(JavaClassDefinition.FromBytes(helperBytes), ClassLoaderTarget.Bootstrap);
        context.Patch(TweakSort);
    }
}
```

Register patches and helpers from `Configure`. The runtime only calls a patch for its class name, applies it as the class loads, logs `patched ...` or `skipped ...: reason`, and, when the agent attaches late and retransformation is available, retransforms the matching classes that are already loaded. See [**Concepts**](concepts.md#class-loaders-and-helper-classes) for where helpers are defined.

### A helper that cannot see the types it is given

A helper in the bootstrap loader cannot see the classes of an application loader, so it cannot declare a parameter of their type. Give `RedirectInvocation` the target as a `JavaMethod` with a descriptor of its own, and let the helper take `java/lang/Object` where the call passes those types; the verifier accepts any reference where `Object` is expected:

```csharp
ClassPatch.For("net/minecraftforge/fml/common/network/handshake/NetworkDispatcher")
    .Method("serverInitiateHandshake", "()I")
    .RedirectInvocation(
        JavaMethod.Virtual("net/minecraft/network/NetworkManager", "func_150723_a", "(Lnet/minecraft/network/EnumConnectionState;)V"),
        JavaMethod.Static("legacyforgepatcher/Network", "setConnectionState", "(Ljava/lang/Object;Ljava/lang/Object;)V"));
```

The target takes the operands of the call in order (the receiver of a virtual or interface call first) and returns what the call returns. Building the patch refuses a target that is not static, takes another number of parameters, returns another type, has another primitive, or has a reference parameter that is neither the type of the operand nor `Object`; a supertype other than `Object` would need the class hierarchy to check, so it is refused too. The result and the log line are those of any redirect. Inside the helper, reach the real types through reflection or a helper defined with `PatchedClassLoader`. This was run with `-Xverify:all` on HotSpot 8 and 17.

### Custom edits that report

`.Edit(Action<ClassFileEditor>)` runs any edit, but cannot say what it did: it always reports `custom edit applied`. `.Edit(description, context => ...)` returns the number of places it changed, and gets the same treatment as the built-in edits: the count in `AppliedEdits`, `<method>: <description> (1 change)` in the result and the log, and a skip (`<description>: nothing to change in <method>`) when it returns 0. It honours `.Method`: a scoped method the class lacks skips the class before the edit runs, and `context.MethodName` and `context.MethodDescriptor` carry the scope, so the edit does not repeat it:

```csharp
ClassPatch.For("com/acme/Service")
    .Method("handle", "(Ljava/lang/String;)I")
    .Edit("string length calls measured", context => context.Editor.ReplaceInvocation(
        JavaMethod.Virtual("java/lang/String", "length", "()I"),
        toOwner: "acme/agent/Strings",
        toDescriptor: "(Ljava/lang/Object;)I",
        methodName: context.MethodName,
        methodDescriptor: context.MethodDescriptor));
```

## Matching several classes

`ClassPatch.For` names one class. These start a patch that targets every class a test accepts, with the same edits and the same all-or-nothing rule per class:

```csharp
ClassPatch.ForPackage("com/acme/service");                              // classes directly in the package
ClassPatch.ForPackage("com/acme", includeSubpackages: true);
ClassPatch.ForMatching(name => name.EndsWith("Controller"), "controllers");   // a filter on the internal name
ClassPatch.ForSubclassesOf("com/acme/Handler");                         // extends or implements it, directly or through known types
ClassPatch.ForAnnotatedWith("com/acme/Traced");                         // class-level annotation, visible at run time or not

ClassPatch.ForPackage("com/acme").WhereSubclassOf("com/acme/Handler").WhereAnnotatedWith("com/acme/Traced");
```

`WhereSubclassOf` and `WhereAnnotatedWith` narrow any patch, and every test must hold. A patch that matches several classes reports the class it patched in each `ClassPatchResult`, while `patch.ClassName` describes what it matches (`package com/acme`). The runtime logs the classes such a patch changed at information level and the ones it skipped at debug level, because most classes it sees lack the target of its edit; a patch that names its class logs every outcome so that a typo shows.

The tests run cheapest first. The name test runs for every class that loads, so keep a filter cheap. The supertype and annotation tests read a lightweight header of the class and run only for classes that pass the name test; `ForSubclassesOf` and `ForAnnotatedWith` have no name test of their own, so they read the header of every class that loads. Start from `ForPackage` when you know the package.

What a supertype test can see is limited by what the agent has seen:

- The runtime learns the direct supertypes of each class from the class files it sees load, and, when a patch looks at supertypes, from the classes that were already loaded when the agent started (at VM initialization or attach) or when the patch was registered. A supertype it has never seen ends the walk up that branch, so a class whose ancestry runs through it does not match.
- A class matches an ancestor, not itself.
- A class-level annotation is read from the class bytes, so for a class that was loaded before the agent attached it cannot be read. A patch that only tests annotations therefore applies to the classes that load later; add a name test (`ForPackage(...).WhereAnnotatedWith(...)`) and the named classes that are already loaded are retransformed and checked.

To test a patch without a JVM, `patch.Matches(classBytes, hierarchy)` says whether it targets a class, and a `ClassHierarchy` you fill by hand stands in for what the runtime knows.

## Advice around a method

`Before` and `After` put a call to your static method before and after a method, which is what a tracer, a timer or an access check needs:

```csharp
ClassPatch Advice = ClassPatch.For("com/acme/Service")
    .Method("handle", "(Ljava/lang/String;I)Ljava/lang/String;")
    .Before(JavaMethod.Static("acme/agent/Tracing", "enter", "(Ljava/lang/Object;Ljava/lang/String;I)V"))
    .After(JavaMethod.Static("acme/agent/Tracing", "leave", "(Ljava/lang/Object;Ljava/lang/String;)Ljava/lang/String;"));
```

The method needs its descriptor, because advice rewrites one method. The hooks are exact:

| Hook | Descriptor |
|---|---|
| `Before` | `(Ljava/lang/Object;` the parameters of the method `)V` |
| `After` | `(Ljava/lang/Object;` the result type if the method has one `)` the result type, or `V` |
| `After(hook, includeArguments: true)` | `(Ljava/lang/Object;` the parameters, then the result type if there is one, `)` the result type, or `V` |

The first parameter is the receiver, `null` for a static method; it may be declared as the class itself (`Lcom/acme/Service;`) instead of `Object`. A mismatch refuses the patch with the descriptor the hook needs. `After` returns the result, so it can replace what the method returns (`Tracing.leave` above returns the string it received, or another one). The classes that declare the hooks must be visible to the advised class, exactly as for `RedirectInvocation`: define them with `ClassLoaderTarget.Bootstrap`, or with `PatchedClassLoader` for classes of application loaders.

How it works decides what advice can and cannot do. The code of the method moves, byte for byte, into a private synthetic method named `handle$jvmbridge$body`, and `handle` becomes a short stub that calls `Before`, the body and `After`. The method keeps its flags (`synchronized` included, so the hooks run inside the monitor), signature, annotations and exception list; the body keeps its stack map frames and line numbers. Consequences:

- A stack trace shows `handle$jvmbridge$body` above `handle`, and `getDeclaredMethods()` lists the body method.
- `After` does not run when the method throws, and a hook that throws stops the call: the exception reaches the caller.
- A method that inspects its caller (`Reflection.getCallerClass`, `@CallerSensitive`) sees the stub as its caller, so do not advise those.
- Constructors and static initializers, native and abstract methods, bridge and synthetic methods, methods of interfaces and methods with more than 255 local variable slots are refused, each with its own `PatchSkipCategory` on the `ClassPatchResult`.
- Advice runs after every other edit of every patch, so a redirect or a constant replacement scoped to the method still sees its original code, in the body.
- A method is advised once: a second patch that advises it is refused with `AlreadyAdvised`. Give `Before` and `After` of one method to one patch.

**Advice applies when the class is defined.** Adding a method to a class that the JVM has loaded is forbidden, so a class that loaded before the agent cannot be advised, and the log says `the class is loaded already and was not advised when it was defined`. Agents that start with `-agentpath` see every class they care about as it defines; an agent that attaches late, or starts from a `-javaagent` jar, only advises classes that load afterwards. Keep advice in a `ClassPatch` of its own: a patch is all or nothing, so one class that cannot be advised refuses the edits that came with it.

A class that was advised when it was defined keeps its methods for as long as it lives. Retransforming it (for any reason, even another agent's) advises it the same way again, and `Unpatch` of the advice turns its stubs into plain calls of the bodies; `PatchNow` puts the hooks back. The body methods stay in the class either way.

## Changing patches while the JVM runs

`Patch` works at any time, not only in `Configure`. Registered after `Configure`, a patch applies to the classes that load from then on and, when retransformation is available, to the matching classes that are already loaded. `PatchNow` does the same and returns a `PatchReport`; `Unpatch` removes a patch and retransforms the classes it had patched, so the JVM transforms them again without it.

```csharp
public sealed class TracingAgent : JavaAgent
{
    private static readonly ClassPatch Tracing = ClassPatch.ForPackage("com/acme/service").ReplaceConstant("quiet", "traced");

    public override void Configure(AgentContext context)
    {
        context.TryEnableRetransformation();   // an attached agent gets this automatically
    }

    public override void OnCommand(AgentContext context, string command)
    {
        PatchReport report = command == "off" ? context.Unpatch(Tracing) : context.PatchNow(Tracing);

        context.Log(report.ToString());
    }
}
```

- Retransformation needs the `can_retransform_classes` capability, which has to be requested before events are enabled: an attached agent gets it automatically, and an agent loaded with `-agentpath` requests it with `TryEnableRetransformation` in `Configure`. Without it the patch still applies to classes that load later, and the report's `NotRetransformedReason` says so.
- The report lists each loaded class that the patch targets and why a class could not be retransformed. A class that the JVM refuses does not stop the others: classes are retransformed in batches, and a batch the JVM rejects is split until the failing class is found.
- Retransformation can only change method bodies, so a patch registered at run time is limited to edits that do that. Declare helper classes in `Configure`; a patch registered later can use the ones that exist.
- Patches apply in the order they were registered. `Unpatch` takes the same `ClassPatch` instance that was registered, and registering one instance twice keeps one copy.
- `PatchNow` and `Unpatch` retransform on the calling thread. From a JVM thread, such as the one that calls `OnCommand`, they need no further setup.
- A patch that tests only annotations, or has no tests of its own, cannot tell which loaded classes it targets, so the report says it applies to classes that load later.

## Helper classes in Java

Add `JavaSource` items to the agent project:

```xml
<ItemGroup>
  <JavaSource Include="java/**/*.java" />
</ItemGroup>
```

The build compiles them with `javac --release $(JavaRelease)` (8 unless you set `JavaRelease`) using the JDK in `JvmBridgeJdkHome` or `JAVA_HOME`, and fails with JVMB004 or JVMB005 when neither names a JDK (version 9 or later) that contains `javac`. To build on a machine with no JDK, set `<JvmBridgeProvisionJdk>true</JvmBridgeProvisionJdk>`: when `JvmBridgeJdkHome` is empty and `JAVA_HOME` has no `javac`, the build downloads the checksum-pinned Temurin 17 that JvmBridge.Testing uses (`JvmBridgeJdkMajor` picks another pinned major, and it follows `JavaRelease` above 17) into a cache under the temporary directory, once per machine, and uses it. It needs `dotnet build` (JVMB009 otherwise) and reports a version without a pinned JDK for the platform as JVMB007 and a failed download as JVMB008; a `JvmBridgeJdkHome` you set is never replaced. See the [**diagnostics catalogue**](../diagnostics.md).

The class files are embedded, and each top-level class becomes an `internal` `JavaClassDefinition` named after its package, with its inner and anonymous classes defined along with it: `context.DefineClass(JavaClasses.Legacyforgepatcher.Collections, ClassLoaderTarget.Bootstrap)` for `legacyforgepatcher/Collections.java`.

## Helper classes in C#

Mark a `static partial` class with `[JavaClass("pkg/Name")]` and its static methods with `[JavaMethod]`. The generator emits a Java class that declares them `native` (named like the C# method with a lower-case first letter, or by `[JavaMethod("name")]`), the unmanaged entry points that call your methods, and a `Definition` property that registers them when the class is defined:

```csharp
[JavaClass("legacyforgepatcher/Callbacks")]
internal static partial class Callbacks
{
    [JavaMethod]
    internal static string Greet(JavaEnvironment environment, string name) => "Hello, " + name;
}

// In Configure:
context.DefineClass(Callbacks.Definition, ClassLoaderTarget.Bootstrap);
```

An optional first `JavaEnvironment` parameter receives the callback's JNI environment. Other parameters and the return type may be `bool`, `sbyte`, `char`, `short`, `int`, `long`, `float`, `double`, `string`, one-dimensional arrays of the primitives and of `string`, or `nint` for a Java object (typed by `[JavaType("Ljava/util/List;")]`, otherwise `java/lang/Object`). An exception that a method throws is reported and contained, and the JVM receives a default value; throw `JavaThrowException` to raise a Java exception instead (see [**Calling between C# and Java**](interop.md)).

## Defining a class by hand

To inject calls into an application without shipping native declarations in its classes, generate a Java class file ([**HelloAgent**](../../samples/HelloAgent) emits its minimal Java 8 helper entirely in C#), then call `environment.DefineClass("jvmbridge/sample/AgentCallbacks", loader.Handle, bytes)` and `environment.RegisterNative(helper.Handle, "onFrame", "(Ljava/lang/Object;)V", callbackPointer)`. `DefineClass` returns a disposable, thread-owned local class reference and clears Java exceptions before raising `JavaException`; defining the same name again in the same loader fails. The loader may be zero for bootstrap definition where the JVM permits it; checked JNI on OpenJ9 requires an explicit loader. Install the helper once per defining loader, and skip instrumenting the helper itself.

## Inspecting loaded classes

In a live JVM callback, use a class reference from that callback or an explicitly owned reference to inspect members and invoke Java code:

```csharp
foreach (JavaFieldInfo field in context.GetDeclaredFields(type))
    Console.WriteLine($"{field.Name}: {field.Descriptor}");

nint fieldId = environment.GetField(type, "profile", "Ljava/lang/Object;");
using JavaLocalReference profile = environment.GetObjectField(instance, fieldId);
nint constructor = environment.GetMethod(type, "<init>", "()V");
using JavaLocalReference created = environment.NewObject(type, constructor, ReadOnlySpan<JvmBridge.Native.jvalue>.Empty);
```

`GetDeclaredMethods` and `GetDeclaredFields` inspect prepared classes in the live JVMTI phase. They copy names, descriptors, modifiers and nullable generic signatures into managed records, and release JVMTI allocations even on failure. The records expose raw IDs; see the ownership rules in [**Concepts**](concepts.md#references-and-ownership).

Override `TransformClass(AgentContext, ClassFile, ClassTransformContext)` to inspect the defining `Loader`, `ProtectionDomain` and `ClassBeingRedefined`. The original two-argument override continues to work.

Raw function pointers are intentionally unsafe escape hatches. Check version, capability and phase requirements before using them. Variadic and `va_list` entries are exposed as addresses; use the typed argument-array `*A` calls from managed code. JvmBridge supplies class bytes, JVMTI transformation plumbing and the small class-file model above, not a general Java bytecode editor.

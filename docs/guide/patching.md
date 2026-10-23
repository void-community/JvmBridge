# Patching classes

`JvmBridge.ClassFiles` reads, edits and writes class files without a JVM, and `ClassPatch` applies such edits declaratively as classes load.

## Class files

`JavaClassFile.Read` exposes the version, flags, hierarchy, fields and methods, the typed constant pool and each `Code` attribute's instructions (offset, opcode, constant operand). It does not interpret stack maps, and it throws `ClassFormatException` for truncated input, trailing bytes, unknown constant tags and malformed code. `file.Invokes(instruction, method)`, `file.AccessesField(instruction, owner, name)` and `file.ReferencesClass(instruction, className)` tell what an instruction refers to without reading the constant pool by hand, and `Opcodes` names every opcode (`Opcodes.InvokeVirtual`, `Opcodes.ALoad0`, `Opcodes.IfICmpEq`), so matching code needs no byte literals.

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

`ReplaceInvocation` turns `invokestatic`, `invokevirtual` or `invokeinterface` into `invokestatic` of another owner at the same length; a virtual or interface receiver becomes the first parameter, and `invokeinterface` padding becomes `nop`. `ClassFileWriter` writes small classes made of static methods (including `native` ones for `RegisterNative`) with a deduplicating constant pool; straight-line code needs no stack map frames. Write bodies with the code builder (see [**Writing bytecode**](#writing-bytecode)). `ModifiedUtf8.GetBytes` encodes class-file strings without the JNI terminator.

## ClassPatch

`ClassPatch.For(className)` describes the edits to one class. It is immutable and reusable. `.Method(name, descriptor)` scopes the edits that follow to one method (`.AnyMethod()` ends the scope); `.RedirectInvocation`, `.ReplaceConstant`, `.ReplaceConstruction`, `.RemoveInvocation`, `.SetAccess`/`.SetFieldAccess` and the `.Edit(editor => ...)` escape hatch add edits. `patch.Apply(bytes)` returns a `ClassPatchResult` that says what changed, or why nothing did, so a test can assert on it without a JVM. A patch is all or nothing: when its method or invocation is missing, the class is left unchanged and the result names the reason.

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

When a redirect, a construction factory or an advice hook names a helper class the agent defines (a `JavaSource` helper, a `[JavaClass]` helper or any `JavaClassDefinition`), `Patch` and `DefineClass` check that the helper declares that static method with that descriptor, whichever of the two comes first, and fail with the patch, the helper and the method named; a typo would otherwise fail with `NoSuchMethodError` only when the patched code runs. A test runs the same check without a JVM with `patch.FindMissingTargets([helper])`.

### A helper that cannot see the types it is given

A helper in the bootstrap loader cannot see the classes of an application loader, so it cannot declare a parameter of their type. Give `RedirectInvocation` the target as a `JavaMethod` with a descriptor of its own, and let the helper take `java/lang/Object` where the call passes those types; the verifier accepts any reference where `Object` is expected:

```csharp
ClassPatch.For("net/minecraftforge/fml/common/network/handshake/NetworkDispatcher")
    .Method("serverInitiateHandshake", "()I")
    .RedirectInvocation(
        JavaMethod.Virtual("net/minecraft/network/NetworkManager", "func_150723_a", "(Lnet/minecraft/network/EnumConnectionState;)V"),
        JavaMethod.Static("legacyforgepatcher/Network", "setConnectionState", "(Ljava/lang/Object;Ljava/lang/Object;)V"));
```

The target takes the operands of the call in order (the receiver of a virtual or interface call first) and returns what the call returns. Building the patch refuses a target that is not static, takes another number of parameters, returns another type, has another primitive, or has a reference parameter that is neither the type of the operand nor `Object`; a supertype other than `Object` would need the class hierarchy to check, so it is refused too. The result and the log line are those of any redirect. Inside the helper, reach the real types through reflection, or better, write the helper against them (see [**A helper that names the application's types**](#a-helper-that-names-the-applications-types)). This was run with `-Xverify:all` on HotSpot 8 and 17.

### A helper that names the application's types

A helper defined with `ClassLoaderTarget.PatchedClassLoader` lives in the loader of the class it patches, so it can take and return the application's own types and the redirect keeps the call's descriptor: no `Object`, no reflection, no wrapper class. Compile it against those types with `JavaCompileReference` items (jars or class directories of the application) or `JavaStubSource` items (stand-ins compiled with it but never embedded); see [**Helper classes in Java**](#helper-classes-in-java). A helper in the same package as the patched class, defined in the same loader, also reaches its package-private members; a private one needs `SetFieldAccess` or `SetMethodAccess` on its class, with the stub declaring it public so that the helper compiles:

```java
// stubs/net/minecraft/network/NetworkManager.java: a JavaStubSource item, compiled but not shipped
package net.minecraft.network;

public class NetworkManager {
    public io.netty.channel.Channel channel;   // private in the game; opened by SetFieldAccess
    public boolean isChannelOpen() { throw new UnsupportedOperationException(); }
}

// java/net/minecraft/network/ConnectionChecks.java: a JavaSource item, in the game's package
package net.minecraft.network;

public final class ConnectionChecks {
    public static boolean isOpenOrPending(NetworkManager manager) {
        return manager.channel == null || manager.isChannelOpen();
    }
}
```

```csharp
context.DefineClass(JavaClasses.Net.Minecraft.Network.ConnectionChecks, ClassLoaderTarget.PatchedClassLoader);
context.Patch(ClassPatch.For("net/minecraft/network/NetworkManager").SetFieldAccess("channel", accessFlags: 0x0001));
context.Patch(ClassPatch.For("net/minecraft/network/ConnectionWatchdog")   // the class whose call is redirected
    .Method("check", "()V")
    .RedirectInvocation(
        JavaMethod.Virtual("net/minecraft/network/NetworkManager", "isChannelOpen", "()Z"),
        JavaMethod.Static("net/minecraft/network/ConnectionChecks", "isOpenOrPending", "(Lnet/minecraft/network/NetworkManager;)Z")));
```

The helper is defined in each loader that defines a class whose patched bytes mention it, before that class runs. [**LegacyAgent**](../../samples/LegacyAgent) does this on Java 6 to 21, with a helper that reads a private field of a package-private class.

### Replacing a construction or removing a call

Two edits that patches otherwise write by matching bytes:

```csharp
ClassPatch.For("net/minecraftforge/fml/common/network/handshake/HandshakeHandler")
    // new ArrayList<>() stored in sentMessages becomes new CopyOnWriteArrayList<>(), with the same constructor arguments.
    .ReplaceConstruction("java/util/ArrayList", "java/util/concurrent/CopyOnWriteArrayList", storedIn: "sentMessages");

ClassPatch.For("com/acme/Registry")
    // new HashSet<>() becomes ConcurrentHashMap.newKeySet(): invokestatic and four nops in place of new, dup and the constructor call.
    .ReplaceConstruction("java/util/HashSet",
        JavaMethod.Static("java/util/concurrent/ConcurrentHashMap", "newKeySet", "()Ljava/util/concurrent/ConcurrentHashMap$KeySetView;"),
        storedIn: "holders");

ClassPatch.For("com/acme/Settings")
    .Method("open", "()V")
    // builder.autosave().autoreload() is builder: each call returns its receiver, so each becomes nops.
    .RemoveInvocation(JavaMethod.Virtual("com/acme/ConfigBuilder", "autosave", "()Lcom/acme/ConfigBuilder;"))
    .RemoveInvocation(JavaMethod.Virtual("com/acme/ConfigBuilder", "autoreload", "()Lcom/acme/ConfigBuilder;"));
```

- `ReplaceConstruction(fromClass, toClass, storedIn)` keeps the constructor's descriptor, so the new class needs a constructor with the same parameters. It matches each `new` to its own constructor call, also when one construction of the class is nested in the arguments of another.
- `ReplaceConstruction(fromClass, factory, storedIn)` only replaces constructions without arguments. The factory must be static, take no parameters and return an object.
- `storedIn` limits either one to objects that go straight into a field of that name (`putfield` or `putstatic`), since one method often creates several objects of a class. Without it, every construction of the class in the scope is replaced.
- `RemoveInvocation(call)` takes a call that has no arguments and returns its own owner, as a fluent setter does; anything else is refused when the patch is built, because removing it would change the stack. Give the owner and descriptor as the call site names them, which `jvmbridge dump` shows.
- The object must fit everywhere the code uses it: the field's type, a cast, a method it is passed to. The verifier checks that when the class loads, so a mismatch fails with `VerifyError` there. With `storedIn`, a factory whose result cannot go into the field at all (a field of a primitive type, or an array type the factory does not return) makes the patch skip the class.

Both edits keep code offsets, so stack map frames stay valid, and they report like the others: `ReplacedConstructions` and `RemovedInvocations` in the result, and a line such as `1 new java/util/ArrayList replaced by new java/util/concurrent/CopyOnWriteArrayList stored in sentMessages` in the log. They were run with `-Xverify:all` on Java 6, 8 and 17.

### Getters, setters and invokers

A helper that must reach a private member, without reflection and without opening it to the whole application, can call a method the patch adds to the class:

```csharp
ClassPatch.For("net/neoforged/fml/loading/FMLConfig")
    .AddFieldGetter(field: "configData", name: "agent$configData", returns: "Ljava/lang/Object;")
    .AddFieldSetter(field: "configData", name: "agent$setConfigData", accepts: "Ljava/lang/Object;")
    .AddInvoker(method: "loadFrom", descriptor: "(Ljava/nio/file/Path;)V", name: "agent$loadFrom");
```

- A getter returns the field, as its own type or, for a reference, a supertype such as `Object`; a setter stores its argument, cast to the field's type. Both are static for a static field, and a final field has no setter. Without `fieldDescriptor`, any field of the name serves, so one getter typed `Object` covers versions of the class whose field types differ.
- An invoker calls a method of the class, private or protected, with the same descriptor: on its receiver without virtual dispatch, or statically for a static method. Constructors are not supported.
- The added methods are public and synthetic, and their names should not collide with the class's own: a class that declares the name already makes the patch skip it.
- Adding a method changes the shape of the class, so additions follow the rules of advice: they are made when the class is first defined, a class that loaded before the agent cannot get them (`SchemaChangeOnRetransform`), and a class keeps them, as they were, whenever it is retransformed, also once the patch is removed. Interfaces get none.
- `ClassPatchResult.AddedMembers` counts them, and the log names each one, for example `agent$configData()Ljava/lang/Object;: getter of field configData added`.

A typed helper calls them by name, compiled against a stub that declares them. They were run with `-Xverify:all` on Java 6, 8 and 17.

### Conditions on a method's code

Versions of an application often differ only in how a method is written. `When` applies the patch only to the version whose code matches:

```csharp
static readonly JavaMethod WriteLock = JavaMethod.Virtual("java/util/concurrent/locks/ReentrantReadWriteLock", "writeLock", "()Ljava/util/concurrent/locks/ReentrantReadWriteLock$WriteLock;");
static readonly JavaMethod Put = JavaMethod.Interface("java/util/Map", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;");
static readonly JavaMethod PutIfAbsent = JavaMethod.Interface("java/util/Map", "putIfAbsent", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;");

ClassPatch.For("net/minecraftforge/eventbus/api/EventListenerHelper")
    .Method("getListenerListInternal", "(Ljava/lang/Class;Z)Lnet/minecraftforge/eventbus/ListenerList;")
    .When(body => body.Calls(WriteLock) && body.Calls(Put) && !body.Calls(PutIfAbsent))
    .RedirectInvocation(Put, toOwner: "acme/agent/EventListeners");
```

- `MethodBody` has three tests: `Calls(method)`, `AccessesField(owner, name)` and `Creates(className)`. Combine them with `&&`, `||` and `!`.
- The condition looks at the method that `Method` scoped the patch to, at every overload when the scope has no descriptor, and at the whole class without a scope. It sees the code as the edits before it left it, so put it first.
- When it fails, the patch skips the class and the reason prints it: `getListenerListInternal(...): requires writeLock() && put() && !putIfAbsent()`.
- It is read when the patch is built: arguments such as `WriteLock` are evaluated then, a malformed method or class name is refused then, and anything that is not one of the tests is refused too. Nothing in it is compiled or runs in the JVM, so it works in a NativeAOT agent.

Conditions only decide whether a patch applies. Edits stay the declarative ones, and run-time behaviour stays in Java helpers.

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

The build compiles them with `javac --release $(JavaRelease)` (8 unless you set `JavaRelease`) using the JDK in `JvmBridgeJdkHome` or `JAVA_HOME`, and fails with JVMB004 or JVMB005 when neither names a JDK (version 9 or later) that contains `javac`. To build on a machine with no JDK, set `<JvmBridgeProvisionJdk>true</JvmBridgeProvisionJdk>`: when `JvmBridgeJdkHome` is empty and `JAVA_HOME` has no `javac`, the build downloads the checksum-pinned Temurin 17 that JvmBridge.Testing uses (`JvmBridgeJdkMajor` picks another pinned major, and it follows `JavaRelease` above 17; `JavaRelease` 6 needs a javac from JDK 9 to 11, so it downloads Temurin 11 and uses it even when `JAVA_HOME` has a javac) into a cache under the temporary directory, once per machine, and uses it. It needs `dotnet build` (JVMB009 otherwise) and reports a version without a pinned JDK for the platform as JVMB007 and a failed download as JVMB008; a `JvmBridgeJdkHome` you set is never replaced. See the [**diagnostics catalogue**](../diagnostics.md).

To name the application's own types, give javac a class path. None of it is embedded:

```xml
<ItemGroup>
  <JavaSource Include="java/**/*.java" />
  <!-- Jars or class directories of the application and its libraries. -->
  <JavaCompileReference Include="refs/client.jar;refs/classes" />
  <!-- Stand-ins for application classes, compiled first. A stub may declare public what the application keeps private. -->
  <JavaStubSource Include="stubs/**/*.java" />
</ItemGroup>
```

The jars of `JavaReference` items (typed proxies, see [**Calling between C# and Java**](interop.md)) are on the class path too. A stub only has to declare what the helpers use, and its bodies never run; javac compiles the helpers against it, and the JVM links them to the real class. Stubs are the way to compile against a member that the application keeps private: declare it public in the stub, and open it at run time with `SetFieldAccess` or `SetMethodAccess`. javac runs with `-implicit:none`, so sources it finds on the class path are not compiled into helpers. Define such helpers with `ClassLoaderTarget.PatchedClassLoader`, which puts them in the loader that can see those types (see [**A helper that names the application's types**](#a-helper-that-names-the-applications-types)).

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

## Writing bytecode

`CodeBuilder` (namespace `JvmBridge.ClassFiles.Code`) writes a method body one instruction at a time, adding the constants each instruction names to the class, so no opcode or constant index is written by hand. `ClassFileWriter.AddMethod` takes it as a function that returns the builder, and fills in `max_stack` and `max_locals` from what it wrote:

```csharp
JavaMethod length = JavaMethod.Virtual("java/lang/String", "length", "()I");

// static int measure(String value) { return 1000 + value.length(); }
byte[] helper = new ClassFileWriter("acme/agent/Strings")
    .AddMethod("measure", "(Ljava/lang/String;)I", code => code.IConst(1000).ALoad(0).Invoke(length).Op(Opcodes.IAdd).IReturn())
    .ToArray();
```

- Constants and locals take their shortest form: `IConst(5)` is `iconst_5`, `IConst(1000)` `sipush`, `IConst(100_000)` an `ldc`, and a local past slot 255 the `wide` form. `Invoke(JavaMethod)` picks `invokestatic`, `invokevirtual` or `invokeinterface` by the method's kind; `InvokeSpecial` calls constructors. `Op(opcode)` writes any instruction without operands.
- Branches go to labels: `CodeLabel done = code.NewLabel(); ... .IfNull(done) ... .Mark(done)`. The builder resolves their offsets and checks that every path reaches a label with the same stack depth.
- It refuses code that pops more than the stack holds, code that no path reaches, and a branch to a label it never places, instead of writing a method the verifier rejects.
- It writes no stack map frames. A class of version 51 (Java 7) or later needs them at every branch target, so `AddMethod` refuses a body with branches there. Write a helper with branches as a `JavaSource` item, or in a class of version 50, which every JVM since Java 6 verifies without frames.
- `ClassFileEditor.AssembleCode(code => ...)` writes instructions against the edited class's constant pool and returns their bytes, for `ReplaceCode` and for comparing with a method's code, without branches.

Tests that need an input class build it the same way. A loop of version 50 and straight-line code of version 52 written with it run with `-Xverify:all` on Java 6, 8 and 17.

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

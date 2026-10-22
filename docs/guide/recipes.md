# Recipes

Short answers to the usual jobs. Each recipe uses the declarative `ClassPatch` API from [**Patching classes**](patching.md); test every patch on class bytes first (see [**Testing an agent**](testing.md)).

## Redirect a call inside one method

Send one call in one method to your own helper. The receiver of a virtual or interface call becomes the helper's first parameter.

```csharp
public sealed class SortAgent : JavaAgent
{
    private static readonly ClassPatch Patch = ClassPatch.For("com/acme/Registry")
        .Method("loadAll", "()V")
        .RedirectInvocation(
            JavaMethod.Static("java/util/Collections", "sort", "(Ljava/util/List;Ljava/util/Comparator;)V"),
            toOwner: "acme/agent/Collections");

    public override void Configure(AgentContext context)
    {
        context.DefineClass(JavaClasses.Acme.Agent.Collections, ClassLoaderTarget.Bootstrap);
        context.Patch(Patch);
    }
}
```

`acme/agent/Collections.java` sits in a `JavaSource` item and declares `public static void sort(List<?> list, Comparator<?> comparator)`. The patch changes the call and nothing else; every other call to `Collections.sort` in `Registry` stays as it was because the patch is scoped to `loadAll`.

## Change a constant

```csharp
ClassPatch.For("com/acme/Config").ReplaceConstant("https://api.acme.com", "http://localhost:8080");
ClassPatch.For("com/acme/Limits").ReplaceConstant(86_400_000, 1_000);
```

String, `int`, `long`, `float` and `double` constants are replaced everywhere in the class; the result reports how many were found. A constant that is not present leaves the class unchanged and says why. Small integers are not constants: the compiler encodes values such as `30` inline in the instructions, so only values it stores in the constant pool (like `86_400_000`) can be replaced this way.

## Open up a private member

```csharp
ClassPatch.For("com/acme/Engine").SetFieldAccess("cache", accessFlags: 0x0001);   // public
ClassPatch.For("com/acme/Engine").Method("start", "()V").SetAccess(0x0001);
```

The flag word is replaced as given, so pass every flag the member should have (`0x0009` for `public static`).

## Call back into C# from patched code

Declare a C# helper whose methods become `native` methods of a generated Java class, and redirect the patched call to it:

```csharp
[JavaClass("acme/agent/Callbacks")]
internal static partial class Callbacks
{
    [JavaMethod]
    internal static string Describe(JavaEnvironment environment, string name) => "seen " + name;
}

// In Configure:
context.DefineClass(Callbacks.Definition, ClassLoaderTarget.Bootstrap);
```

Redirect the call to `acme/agent/Callbacks.describe` as in the first recipe. Parameters and results may be primitives, `string` and `nint`; see [**Helper classes in C#**](patching.md#helper-classes-in-c).

## Patch every class that implements an interface

```csharp
ClassPatch.ForPackage("com/acme/handlers").WhereSubclassOf("com/acme/Handler").ReplaceConstant("v1", "v2");
```

Every class in the package that implements `Handler`, directly or through a type the agent knows, gets the edit, and the ones without the constant are skipped quietly. See [**Matching several classes**](patching.md#matching-several-classes).

## Turn a patch on and off while the JVM runs

Keep the `ClassPatch` in a field, register it with `PatchNow` and remove it with `Unpatch` from `OnCommand`, then drive it with `jcmd <pid> JVMTI.agent_load /path/MyAgent.so on` or `off`. The classes that are already loaded are retransformed each time. See [**Changing patches while the JVM runs**](patching.md#changing-patches-while-the-jvm-runs).

## Make the agent verbose without rebuilding

```sh
java -agentpath:/path/MyAgent.so=log=debug -jar app.jar
```

```csharp
context.Log(AgentLogLevel.Debug, "inspected " + name);
if (context.ParsedOptions.IsEnabled("trace-classes")) { /* extra work */ }
```

## Time a method or log its arguments

```csharp
private static readonly ClassPatch Timing = ClassPatch.For("com/acme/Service")
    .Method("handle", "(Ljava/lang/String;I)Ljava/lang/String;")
    .Before(JavaMethod.Static("acme/agent/Timing", "begin", "(Ljava/lang/Object;Ljava/lang/String;I)V"))
    .After(JavaMethod.Static("acme/agent/Timing", "end", "(Ljava/lang/Object;Ljava/lang/String;)Ljava/lang/String;"));
```

```java
// acme/agent/Timing.java, a JavaSource item defined with ClassLoaderTarget.Bootstrap
public final class Timing {
    private static final ThreadLocal<long[]> STARTED = new ThreadLocal<long[]>();

    public static void begin(Object self, String name, int attempt) {
        System.err.println("handle(" + name + ", " + attempt + ")");
        STARTED.set(new long[] {System.nanoTime()});
    }

    public static String end(Object self, String result) {
        System.err.println("handle took " + (System.nanoTime() - STARTED.get()[0]) / 1000 + " us");
        return result;
    }
}
```

`After` does not run when the method throws, so a timer that must see failures keeps its state in a thread-local that the next `Before` overwrites, as above. Hooks run on the thread that calls the method, inside any monitor the method holds, so keep them short and never call the advised method from them.

## Hook a constructor

Advice does not cover constructors: the JVM verifies the code before and after `super(...)` in a way that a forwarding stub cannot satisfy, and the code of a constructor cannot move into a method of its own. What works instead:

- Advise the **factory** that application code calls, if there is one (`Service.create(...)`, `Builder.build()`): its `After` hook receives the new object as the result and may return another one.
- Advise the **first method** the object is used through, and have the hook do its work once per object (keep a `WeakHashMap` or an `IdentityHashMap` of the objects it has seen).
- To run code when a class first loads, advise nothing: use `OnClassPrepare` or the `ClassLoad` event, see [**JVM events**](events.md).

## Replace what a method returns

```csharp
ClassPatch.For("com/acme/Pricing")
    .Method("discount", "(I)I")
    .After(JavaMethod.Static("acme/agent/Pricing", "cap", "(Ljava/lang/Object;I)I"));
```

`cap(Object self, int result)` returns the value the caller sees. To decide from the arguments too, pass `includeArguments: true` and declare them: `cap(Object self, int amount, int result)`.

## Patch a class of the JDK (Java 9 and later)

Classes of `java.base` are patched like any other; the one rule is where the helper lives. A patched `java.base` class can call a helper that the agent defines in the **bootstrap** loader, and cannot call one defined in an application loader.

```csharp
private static readonly ClassPatch Joiner = ClassPatch.For("java/util/StringJoiner")
    .RedirectInvocation(
        JavaMethod.Static("java/util/Objects", "requireNonNull", "(Ljava/lang/Object;Ljava/lang/String;)Ljava/lang/Object;"),
        toOwner: "acme/agent/Checks");

public override void Configure(AgentContext context)
{
    context.DefineClass(JavaClasses.Acme.Agent.Checks, ClassLoaderTarget.Bootstrap);
    context.Patch(Joiner);
}
```

This is the `JavaBase` patch of the HelloAgent sample. It was run against HotSpot 8, 17 and 21, where `java.base` is a named module from Java 9: at startup with `-agentpath`, and through a late attach, which retransforms the class that is already loaded. It needed no `--add-opens`, `--add-exports` or other module option on any of them. Keep these in mind:

- JDK classes load early and are called from everywhere. Keep the redirect to the one method you mean (`.Method(...)`), and keep the helper free of anything that could call back into the patched method.
- A patched class of the bootstrap loader that also mentions a `ClassLoaderTarget.PatchedClassLoader` helper is left unpatched, because that helper cannot be defined there. The log says so.
- Retransforming a JDK class changes method bodies only, like any retransformation.

## Run more than one agent in a JVM

Agents are separate: each load of a library gets its own JVMTI environment, and each library carries its own .NET runtime, so two agents do not share state. A few things are shared by the JVM and are worth knowing:

- **Other agents' transformations.** JVMTI chains the class-file hooks of the agents, so each sees the bytes the previous one produced (agents that cannot retransform are called before those that can, the rest in load order). Your patches see bytes another agent has already changed, and the reverse. Patch narrowly (a method, a constant) so that another agent's edit to the same class does not make yours skip, and read the `ClassPatchResult` log lines when they do.
- **Retransformation.** When any agent retransforms a class, every agent's transformer runs again on the original bytes. An agent whose patches are deterministic gets the same result again, which is what `ClassPatch` provides.
- **Two copies of one agent.** Loading the same library twice (`-agentpath:/a/MyAgent.so -agentpath:/b/MyAgent.so`) starts two runtimes. Both load and unload and the process exits normally. On HotSpot the second reports `LinkageError ... duplicate class definition` for the helper classes that the first defined in the same loader, and skips them; OpenJ9 puts bootstrap helpers in jars on the bootstrap search path, where two copies do not conflict. Loading the same file again after the agent runs is a command, not a second agent, see [**Telling a running agent what to do**](attaching.md#telling-a-running-agent-what-to-do).
- **Jars.** `-javaagent` jars from `jvmbridge pack` each carry a launcher of their own, so any number of them can be named. Naming the same jar twice starts its agent once.

The integration tests run the two-copies case and the same-jar-twice case on every JVM of the CI matrix.

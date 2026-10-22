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

## Make the agent verbose without rebuilding

```sh
java -agentpath:/path/MyAgent.so=log=debug -jar app.jar
```

```csharp
context.Log(AgentLogLevel.Debug, "inspected " + name);
if (context.ParsedOptions.IsEnabled("trace-classes")) { /* extra work */ }
```

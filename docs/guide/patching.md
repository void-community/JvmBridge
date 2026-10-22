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

An optional first `JavaEnvironment` parameter receives the callback's JNI environment. Other parameters and the return type may be `bool`, `sbyte`, `char`, `short`, `int`, `long`, `float`, `double`, `string`, or `nint` for a Java object (typed by `[JavaType("Ljava/util/List;")]`, otherwise `java/lang/Object`). An exception that a method throws is reported and contained, and the JVM receives a default value.

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

# Calling between C# and Java

## Arrays, exceptions, monitors and local frames

`JavaEnvironment` has extension methods in `JvmBridge.Runtime` for the parts of JNI that agents need most:

```csharp
using JavaLocalReference bytes = environment.NewArray<byte>(span);              // Java byte[] from a ReadOnlySpan<byte>
byte[] copy = environment.ToArray<byte>(bytes.Handle);                          // and back
environment.CopyTo(bytes.Handle, start: 0, destination);                        // into a Span<byte>
environment.CopyFrom(bytes.Handle, start: 0, source);                           // from a ReadOnlySpan<byte>
int length = environment.GetArrayLength(bytes.Handle);

using JavaLocalReference names = environment.NewStringArray(["a", "b"]);        // String[]
string?[] values = environment.GetStringArray(names.Handle);

environment.ThrowNew("java/lang/IllegalStateException", "message");             // raises it in the calling Java thread
using JavaMonitor monitor = environment.MonitorEnter(target);                   // synchronized (target) { ... }
using JavaLocalFrame frame = environment.PushLocalFrame(capacity: 64);          // releases the locals created inside it
```

Element types are `bool`, `sbyte` and `byte` (both Java `byte`), `char`, `short`, `int`, `long`, `float` and `double`. A Java exception, such as an index out of range, surfaces as a `JavaException`. After `Throw` or `ThrowNew` an exception is pending in the current thread, and only a few JNI functions may be called until native code returns to Java.

## [JavaMethod] with arrays and exceptions

A `[JavaMethod]` method (see [**Helper classes in C#**](patching.md#helper-classes-in-c)) may take and return one-dimensional arrays of the primitives and of `string`; they map to the Java array types (`[I`, `[B`, `[Ljava/lang/String;`). A null array crosses as null. To raise a Java exception from such a method, throw `JavaThrowException`:

```csharp
[JavaClass("acme/agent/Checks")]
internal static partial class Checks
{
    [JavaMethod]
    internal static byte[] Reverse(byte[] values) => [.. values.Reverse()];

    [JavaMethod]
    internal static int Positive(int value) =>
        value > 0 ? value : throw new JavaThrowException("java/lang/IllegalArgumentException", "must be positive");
}
```

The generated entry point raises the named exception in the caller and returns a default value. Any other exception is still contained and reported, because an unmanaged entry point never lets one escape.

## Typed calls into an application

Add a `JavaReference` for each jar whose classes the agent calls, with the classes to wrap in `Types`, and call them without descriptor strings:

```xml
<ItemGroup>
  <JavaReference Include="libs/app.jar" Types="com/acme/Service;com/acme/model/*" />
</ItemGroup>
```

`Types` entries are separated by semicolons: an internal class name, a package (`com/acme/*`), or a package and its subpackages (`com/acme/**`). The build writes a struct per class into `JavaProxies.<Package>`:

```csharp
using JavaProxies.Com.Acme;

using JavaLocalReference created = Service.New(environment, "name");     // constructor
Service service = new(created.Handle);                                   // wrap a reference you already have
int total = Service.Count(environment, 3);                               // static method
string? label = service.Label(environment);                              // instance method
using JavaLocalReference child = service.Child(environment, 1L, true);   // object result
```

- Primitives and `String` map to C# types, and any other object or array is a `nint` reference on the way in and a `JavaLocalReference` on the way out. A `String` argument may be null.
- Only public methods and constructors of public, top-level classes are wrapped; nested classes, fields and synthetic or bridge methods are not.
- Overloads that differ only in the Java type of an object argument get a suffix from their parameter types (`Send_Object`, `Send_Runnable`).
- Constructors are `New`. A member whose name would clash with the struct (`Handle`, `ClassName`, or the class name itself) gets a trailing underscore.
- A call looks the method up each time, and the class is found with `FindClass`, so it must be visible to the system class loader; an instance call uses the object's own class.
- The wrapper does not own its reference. It is as long-lived as the reference you pass: promote it to a global reference to keep it.
- `JavaReference` needs `dotnet build`. The build reports a missing jar (JVMB013), an item without `Types` (JVMB011) and a `Types` entry that matches nothing (JVMB012).

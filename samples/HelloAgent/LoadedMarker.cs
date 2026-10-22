using JvmBridge.Native;
using JvmBridge.Runtime;

namespace HelloAgent;

/// <summary>Sets the system property that tells the application the agent is loaded. A JVM that starts the agent after VM initialization, as <c>-javaagent</c> does, does not deliver the VM initialization event on every JVM (OpenJ9 does not), so the attach path sets it too.</summary>
internal static unsafe class LoadedMarker
{
    internal static void Mark(JavaEnvironment environment)
    {
        using JavaLocalReference system = environment.FindClass(name: "java/lang/System");

        nint method = environment.GetMethod(system.Handle, name: "setProperty", signature: "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", isStatic: true);

        using JavaLocalReference name = environment.NewString(value: "jvmbridge.loaded");

        using JavaLocalReference value = environment.NewString(value: "true");

        jvalue[] arguments = [new() { l = (_jobject*)name.Handle }, new() { l = (_jobject*)value.Handle }];

        using JavaLocalReference previous = environment.CallObject(system.Handle, method, arguments, isStatic: true);
    }
}

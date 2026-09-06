using JvmBridge.Native;
using JvmBridge.Runtime;

if (args.Length == 1 && args[0] == "--abi")
{
    AbiInspector.Write();
    return;
}

if (args.Length != 1)
    throw new ArgumentException("Pass the absolute path to the JVM's native library.");
using JavaVirtualMachine machine = JavaVirtualMachine.Create(args[0], "-Xcheck:jni");
using JavaThreadAttachment attachment = machine.AttachCurrentThread();
JavaEnvironment environment = attachment.Environment;
using JavaLocalReference system = environment.FindClass("java/lang/System");
nint method = environment.GetMethod(system.Handle, "getProperty", "(Ljava/lang/String;)Ljava/lang/String;", true);
using JavaLocalReference key = environment.NewString("java.version");
unsafe
{
    jvalue[] arguments = [new() { l = (_jobject*)key.Handle }];
    using JavaLocalReference value = environment.CallObject(system.Handle, method, arguments, true);
    Console.WriteLine($"Java version: {environment.GetString(value.Handle)}");
    Console.WriteLine($"ABI JNI={sizeof(JNINativeInterface_)} JVMTI={sizeof(jvmtiInterface_1_)} capabilities={sizeof(jvmtiCapabilities)} value={sizeof(jvalue)}");
    Console.WriteLine($"SLOTS GetArrayLength={System.Runtime.InteropServices.Marshal.OffsetOf<JNINativeInterface_>(nameof(JNINativeInterface_.GetArrayLength)).ToInt64() / IntPtr.Size} GetJavaVM={System.Runtime.InteropServices.Marshal.OffsetOf<JNINativeInterface_>(nameof(JNINativeInterface_.GetJavaVM)).ToInt64() / IntPtr.Size}");
}
foreach (string text in new[] { "A\0B", "\uD83D\uDE00", "\uD800", "" })
{
    using JavaLocalReference value = environment.NewString(text);
    if (environment.GetString(value.Handle) != text)
        throw new InvalidOperationException("UTF-16 round trip failed.");
}
try
{
    using JavaLocalReference missing = environment.FindClass("jvmbridge/DefinitelyMissingClass");
    throw new InvalidOperationException("Java exception was not propagated.");
}
catch (JavaException)
{
    using JavaLocalReference recovered = environment.NewString("recovered");
    if (environment.GetString(recovered.Handle) != "recovered")
        throw new InvalidOperationException("JNI exception was not cleared.");
}
Console.WriteLine("HOST_OK");

using JvmBridge.Native;
using JvmBridge.Runtime;

if (args.Length == 1 && args[0] == "--abi")
{
    AbiInspector.Write();

    return;
}

if (args.Length is < 1 or > 2)
    throw new ArgumentException(message: "Pass the absolute path to the JVM's native library.");

JavaVirtualMachine machine = JavaVirtualMachine.Create(args[0], options: ["-Xcheck:jni"]);
int expectedDestroyResult = args.Length == 2 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 0;

int destroyResult = 0;

try
{
    using JavaThreadAttachment attachment = machine.AttachCurrentThread();

    JavaEnvironment environment = attachment.Environment;

    using JavaLocalReference system = environment.FindClass(name: "java/lang/System");

    nint method = environment.GetMethod(system.Handle, name: "getProperty", signature: "(Ljava/lang/String;)Ljava/lang/String;", isStatic: true);

    using JavaLocalReference key = environment.NewString(value: "java.version");

    unsafe
    {
        jvalue[] arguments = [new() { l = (_jobject*)key.Handle }];

        using JavaLocalReference value = environment.CallObject(system.Handle, method, arguments, isStatic: true);

        Console.WriteLine($"Java version: {environment.GetString(value.Handle)}");
        Console.WriteLine(
            $"ABI JNI={sizeof(JNINativeInterface_)} JVMTI={sizeof(jvmtiInterface_1_)} capabilities={sizeof(jvmtiCapabilities)} value={sizeof(jvalue)}"
        );
        Console.WriteLine(
            $"SLOTS GetArrayLength={System.Runtime.InteropServices.Marshal.OffsetOf<JNINativeInterface_>(nameof(JNINativeInterface_.GetArrayLength)).ToInt64() / IntPtr.Size} GetJavaVM={System.Runtime.InteropServices.Marshal.OffsetOf<JNINativeInterface_>(nameof(JNINativeInterface_.GetJavaVM)).ToInt64() / IntPtr.Size}"
        );
    }

    foreach (string text in new[] { "A\0B", "\uD83D\uDE00", "\uD800", "" })
    {
        using JavaLocalReference value = environment.NewString(text);

        if (environment.GetString(value.Handle) != text)
            throw new InvalidOperationException(message: "UTF-16 round trip failed.");
    }

    WriteStatus(message: "HOST_UNICODE_OK");

    try
    {
        WriteStatus(message: "HOST_LOOKUP_START");

        using JavaLocalReference missing = environment.FindClass(name: "jvmbridge/DefinitelyMissingClass");

        throw new InvalidOperationException(message: "Java exception was not propagated.");
    }
    catch (JavaException exception)
    {
        WriteStatus(message: "HOST_LOOKUP_CAPTURED");

        bool missingDiagnostics = exception.Operation != nameof(JavaEnvironment.FindClass)
            || string.IsNullOrEmpty(exception.JavaTypeName)
            || string.IsNullOrEmpty(exception.JavaStackTrace);

        if (missingDiagnostics)
            throw new InvalidOperationException(message: "Embedded JVM did not capture the Java lookup diagnostics.");

        using JavaLocalReference recovered = environment.NewString(value: "recovered");

        if (environment.GetString(recovered.Handle) != "recovered")
            throw new InvalidOperationException(message: "JNI exception was not cleared.");
    }

    WriteStatus(message: "HOST_OK");
}
finally
{
    try
    {
        machine.Dispose();
    }
    catch (JniException exception) when (exception.Operation == nameof(JavaVirtualMachine.Dispose))
    {
        destroyResult = exception.ErrorCode;
    }
}

if (destroyResult != expectedDestroyResult)
    throw new InvalidOperationException(message: "Managed JVM shutdown differed from the native C baseline.");

Console.WriteLine($"DESTROY_RESULT={destroyResult}");

if (destroyResult != 0)
    WriteStatus(message: "The native C baseline returned the same shutdown error; this JVM does not demonstrate successful embedded shutdown.");

// These invariant markers form the integration-test output protocol.
static void WriteStatus(string message)
{
    Console.WriteLine(message);
}

using JvmBridge.ClassFiles;

namespace HelloAgent;

internal static class CallbackClassFile
{
    internal const string Name = "jvmbridge/sample/AgentCallbacks";
    private const string Signature = "(Ljava/lang/Object;)V";

    internal static byte[] Generate()
    {
        return new ClassFileWriter(Name).AddNativeMethod(name: "onFrame", Signature).ToArray();
    }

    internal static byte[] Inject(byte[] original)
    {
        ClassFileEditor editor = new(original);

        // The fixture's probe is: aload_0, invokevirtual Object.hashCode()I, pop, return.
        // The callback returns nothing, so the call is redirected and the pop becomes a nop.
        int redirected = editor.ReplaceInvocation(
            JavaMethod.Virtual(owner: "java/lang/Object", name: "hashCode", descriptor: "()I"),
            Name,
            toName: "onFrame",
            Signature,
            methodName: "probe",
            Signature
        );

        bool discarded = editor.ReplaceCode(methodName: "probe", Signature, codeOffset: 4, [0x57], [0x00]);

        return redirected != 1 || !discarded
            ? throw new InvalidOperationException(message: "Expected Java 8 fixture probe body was not found.")
            : editor.ToArray();
    }
}

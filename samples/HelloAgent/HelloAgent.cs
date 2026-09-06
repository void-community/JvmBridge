using System.Runtime.InteropServices;
using System.Text;
using JvmBridge.Agents;
using JvmBridge.Native;
using JvmBridge.Runtime;

public sealed unsafe class HelloAgent : JavaAgent
{
    private static readonly object _logLock = new();
    private static AgentContext? _context;

    public override void OnLoad(AgentContext context)
    {
        _context = context;
        if (context.Options.Contains("fail-start", StringComparison.Ordinal))
            throw new InvalidOperationException("Intentional initialization failure.");
        bool retransform = context.TryEnableRetransformation();
        Log($"LOAD retransform={retransform}");
    }

    public override void OnAttach(AgentContext context)
    {
        OnLoad(context);
        Log("ATTACH");
        using JavaThreadAttachment attachment = context.VirtualMachine.AttachCurrentThread();
        JavaEnvironment environment = attachment.Environment;
        foreach (nint type in context.GetLoadedClasses(environment))
        {
            try
            {
                if (context.GetClassSignature(type) == "LBridgeFixture;")
                {
                    Register(environment, type);
                    if (context.CanRetransform)
                        context.Retransform(type);
                }
            }
            finally { environment.Functions->DeleteLocalRef((JNINativeInterface_**)environment.Handle, (_jobject*)type); }
        }
    }

    public override void OnVmInit(AgentContext context, JavaEnvironment environment)
    {
        Log("VM_INIT");
        using JavaLocalReference system = environment.FindClass("java/lang/System");
        nint method = environment.GetMethod(system.Handle, "setProperty", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", true);
        using JavaLocalReference name = environment.NewString("jvmbridge.loaded");
        using JavaLocalReference value = environment.NewString("true");
        jvalue[] arguments = [new() { l = (_jobject*)name.Handle }, new() { l = (_jobject*)value.Handle }];
        using JavaLocalReference previous = environment.CallObject(system.Handle, method, arguments, true);
        if (context.Options.Contains("fail-callback", StringComparison.Ordinal))
            throw new InvalidOperationException("Intentional callback failure.");
    }

    public override void OnClassPrepare(AgentContext context, JavaEnvironment environment, nint type)
    {
        if (context.GetClassSignature(type) == "LBridgeFixture;")
            Register(environment, type);
    }

    private static void Register(JavaEnvironment environment, nint type)
    {
        environment.RegisterNative(type, "roundTrip", "(Ljava/lang/String;)Ljava/lang/String;", (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, _jobject*>)&RoundTrip);
        environment.RegisterNative(type, "retransform", "(Ljava/lang/Class;)Z", (nint)(delegate* unmanaged<JNINativeInterface_**, _jobject*, _jobject*, byte>)&Retransform);
        Log("REGISTER_NATIVES");
    }

    [UnmanagedCallersOnly]
    private static _jobject* RoundTrip(JNINativeInterface_** nativeEnvironment, _jobject* type, _jobject* value)
    {
        try
        {
            using JavaEnvironment environment = new((nint)nativeEnvironment);
            using JavaLocalReference local = environment.NewLocalReference((nint)value);
            using JavaGlobalReference global = local.ToGlobal();
            string? result = null;
            Exception? failure = null;
            JavaVirtualMachine machine = environment.GetVirtualMachine();
            Thread worker = new(() =>
            {
                try
                {
                    using JavaThreadAttachment attachment = machine.AttachCurrentThread();
                    result = attachment.Environment.GetString(global.Handle);
                }
                catch (Exception exception) { failure = exception; }
            });
            worker.Start();
            worker.Join();
            if (failure is not null)
                throw failure;
            using JavaLocalReference converted = environment.NewString(result ?? string.Empty);
            return environment.Functions->NewLocalRef(nativeEnvironment, (_jobject*)converted.Handle);
        }
        catch (Exception exception)
        {
            Log($"NATIVE_ERROR {exception.Message}");
            return null;
        }
    }

    [UnmanagedCallersOnly]
    private static byte Retransform(JNINativeInterface_** environment, _jobject* caller, _jobject* type)
    {
        try
        {
            if (_context is not { CanRetransform: true } context)
                return 0;
            context.Retransform((nint)type);
            return 1;
        }
        catch (Exception exception) { Log($"NATIVE_ERROR {exception.Message}"); return 0; }
    }

    public override byte[]? TransformClass(AgentContext context, ClassFile file)
    {
        if (file.Name != "BridgeFixture")
            return null;
        // The fixture's unique constant has equal-length replacements. This is
        // intentionally an example, not a general Java bytecode rewriting API.
        byte[] result = file.Bytes.ToArray();
        byte[] original = Encoding.ASCII.GetBytes("ORIGINAL");
        byte[] replacement = Encoding.ASCII.GetBytes(file.IsRetransformation ? "RELOADED" : "MODIFIED");
        int index = result.AsSpan().IndexOf(original);
        if (index < 0)
            throw new InvalidOperationException("Fixture constant was not found.");
        replacement.CopyTo(result, index);
        Log(file.IsRetransformation ? "RETRANSFORM" : "TRANSFORM");
        return result;
    }

    public override void OnVmDeath(AgentContext context, JavaEnvironment environment) => Log("VM_DEATH");
    public override void OnUnload(AgentContext context) => Log("UNLOAD");
    private static void Log(string message)
    {
        lock (_logLock)
            Console.Error.WriteLine($"JvmBridge sample: {message}");
    }
}

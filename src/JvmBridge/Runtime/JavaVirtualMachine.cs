using System.Runtime.InteropServices;
using JvmBridge.Native;

namespace JvmBridge.Runtime;

/// <summary>A JVM handle. Only a machine created by Create is destroyed by Dispose.</summary>
public sealed unsafe class JavaVirtualMachine : IDisposable
{
    private readonly JNIInvokeInterface_** _machine;
    private readonly bool _owned;
    private bool _disposed;
    private JavaVirtualMachine(nint machine, bool owned) { ArgumentOutOfRangeException.ThrowIfZero(machine); _machine = (JNIInvokeInterface_**)machine; _owned = owned; }
    public nint Handle { get { ObjectDisposedException.ThrowIf(_disposed, this); return (nint)_machine; } }
    public static JavaVirtualMachine Borrow(nint machine) => new(machine, false);

    public static JavaVirtualMachine Create(string libraryPath, params string[] options)
    {
        nint library = NativeJvmLibrary.Load(Path.GetFullPath(libraryPath));
        // A JVM library stays loaded for process lifetime; unloading it is not supported here.
        var create = (delegate* unmanaged<JNIInvokeInterface_***, void**, JavaVMInitArgs*, int>)NativeLibrary.GetExport(library, "JNI_CreateJavaVM");
        List<nint> strings = new();
        try
        {
            JavaVMOption[] nativeOptions = new JavaVMOption[options.Length];
            for (int index = 0; index < options.Length; index++)
            {
                byte[] bytes = ModifiedUtf8.Encode(options[index]);
                nint address = Marshal.AllocHGlobal(bytes.Length);
                strings.Add(address);
                Marshal.Copy(bytes, 0, address, bytes.Length);
                nativeOptions[index].optionString = (byte*)address;
            }
            fixed (JavaVMOption* arguments = nativeOptions)
            {
                JavaVMInitArgs configuration = new() { version = Methods.JNI_VERSION_1_8, nOptions = nativeOptions.Length, options = arguments };
                JNIInvokeInterface_** machine = null;
                void* environment = null;
                Check(create(&machine, &environment, &configuration), nameof(Create));
                return new JavaVirtualMachine((nint)machine, true);
            }
        }
        finally
        {
            foreach (nint address in strings)
                Marshal.FreeHGlobal(address);
        }
    }

    public JavaThreadAttachment AttachCurrentThread()
    {
        _ = Handle;
        void* environment = null;
        int result = (*_machine)->GetEnv(_machine, &environment, Methods.JNI_VERSION_1_8);
        bool attached = result == Methods.JNI_EDETACHED;
        if (attached)
            result = (*_machine)->AttachCurrentThreadAsDaemon(_machine, &environment, null);
        Check(result, nameof(AttachCurrentThread));
        return new JavaThreadAttachment(this, new JavaEnvironment((nint)environment), attached);
    }

    public nint GetToolingEnvironment(int version = (int)Methods.JVMTI_VERSION_1_2)
    {
        _ = Handle;
        void* environment = null;
        Check((*_machine)->GetEnv(_machine, &environment, version), nameof(GetToolingEnvironment));
        return (nint)environment;
    }

    internal void DetachCurrentThread() => Check((*_machine)->DetachCurrentThread(_machine), nameof(DetachCurrentThread));
    internal static void Check(int result, string operation)
    {
        if (result != Methods.JNI_OK)
            throw new JniException(result, operation);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (_owned)
            Check((*_machine)->DestroyJavaVM(_machine), nameof(Dispose));
        _disposed = true;
    }
}

/// <summary>Detaches only threads attached by this scope. Dispose on the creating thread.</summary>
public sealed class JavaThreadAttachment : IDisposable
{
    private readonly JavaVirtualMachine _machine;
    private readonly bool _attached;
    private bool _disposed;
    public JavaEnvironment Environment { get; }
    internal JavaThreadAttachment(JavaVirtualMachine machine, JavaEnvironment environment, bool attached) { _machine = machine; Environment = environment; _attached = attached; }
    public void Dispose()
    {
        if (_disposed)
            return;
        Environment.EnsureAccessible();
        if (_attached)
            _machine.DetachCurrentThread();
        Environment.Dispose();
        _disposed = true;
    }
}

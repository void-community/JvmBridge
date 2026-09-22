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

    /// <summary>Gets the raw JVM invocation-interface pointer while this wrapper remains active.</summary>
    public nint Handle { get { EnsureAccessible(); return (nint)_machine; } }

    /// <summary>Creates a wrapper that does not own or destroy an existing JVM.</summary>
    /// <param name="machine">The nonzero raw JVM invocation-interface pointer.</param>
    /// <returns>A borrowed JVM wrapper.</returns>
    public static JavaVirtualMachine Borrow(nint machine) => new(machine, false);

    /// <summary>Loads a JVM library and creates an owned JVM using modified UTF-8 option strings.</summary>
    /// <param name="libraryPath">The path to the native JVM library.</param>
    /// <param name="options">The JVM startup options.</param>
    /// <returns>An owned JVM wrapper that destroys the machine when disposed.</returns>
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

    /// <summary>Gets an environment for the current thread, attaching it as a daemon when necessary.</summary>
    /// <returns>An attachment scope that detaches only when this call attached the thread.</returns>
    public JavaThreadAttachment AttachCurrentThread()
    {
        EnsureAccessible();
        void* environment = null;
        int result = (*_machine)->GetEnv(_machine, &environment, Methods.JNI_VERSION_1_8);
        bool attached = result == Methods.JNI_EDETACHED;
        if (attached)
            result = (*_machine)->AttachCurrentThreadAsDaemon(_machine, &environment, null);
        Check(result, nameof(AttachCurrentThread));
        return new JavaThreadAttachment(this, new JavaEnvironment((nint)environment), attached);
    }

    /// <summary>Gets a tooling environment for the requested JVMTI version.</summary>
    /// <param name="version">The JVMTI version to request.</param>
    /// <returns>The raw tooling-environment pointer.</returns>
    public nint GetToolingEnvironment(int version = (int)Methods.JVMTI_VERSION_1_2)
    {
        EnsureAccessible();
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

    private void EnsureAccessible() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc/>
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

    /// <summary>Gets the JNI environment borrowed for the current thread and attachment scope.</summary>
    public JavaEnvironment Environment { get; }
    internal JavaThreadAttachment(JavaVirtualMachine machine, JavaEnvironment environment, bool attached) { _machine = machine; Environment = environment; _attached = attached; }
    /// <inheritdoc/>
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

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace JvmBridge.Runtime;

internal static unsafe class WindowsX86JvmCompatibility
{
    internal static void Install(nint jvm)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4 || !NativeLibrary.TryGetExport(jvm, "gHotSpotVMStructs", out _))
            return;

        nint library = NativeLibrary.Load("JvmBridge.WindowsX86.dll", typeof(WindowsX86JvmCompatibility).Assembly, DllImportSearchPath.SafeDirectories | DllImportSearchPath.AssemblyDirectory);
        try
        {
            var install = (delegate* unmanaged[Cdecl]<nint, uint>)NativeLibrary.GetExport(library, "JvmBridgeInstallSafeFetchHandler");
            uint error = install(jvm);
            if (error != 0)
                throw new Win32Exception((int)error, "Could not install the Windows x86 JVM compatibility handler.");
        }
        finally
        {
            // A successful registration pins the native handler for process lifetime.
            NativeLibrary.Free(library);
        }
    }
}

using System.Runtime.InteropServices;

namespace JvmBridge.Agents;

/// <summary>Retains the NativeAOT module when a JVM releases its agent-library handle.</summary>
internal static unsafe partial class NativeModule
{
    private static nint s_moduleHandle;

    internal static void Retain(nint entryPoint)
    {
        if (Volatile.Read(ref s_moduleHandle) != 0)
            return;

        nint moduleHandle;

        if (OperatingSystem.IsWindows())
        {
            // FROM_ADDRESS | PIN: the NativeAOT runtime cannot be unloaded.
            if (!GetModuleHandleEx(flags: 5, entryPoint, out moduleHandle))
                throw new InvalidOperationException($"Unable to pin agent module: {Marshal.GetLastPInvokeError()}.");
        }
        else
        {
            delegate* unmanaged<nint, ModuleInformation*, int> resolve = (delegate* unmanaged<nint, ModuleInformation*, int>)NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), name: "dladdr");
            ModuleInformation information = default;

            if (resolve(entryPoint, &information) == 0 || information.FileName == 0)
                throw new InvalidOperationException(message: "Unable to identify the native agent module.");

            string path = Marshal.PtrToStringUTF8(information.FileName) ?? throw new InvalidOperationException(message: "Native module path is unavailable.");
            // Retain an independent dlopen reference for process lifetime.
            // OpenJ9 may dlclose its own handle during DestroyJavaVM.
            moduleHandle = NativeLibrary.Load(path);
        }

        // Concurrent first calls may retain the same module more than once. Every
        // retained handle intentionally lives until process exit, so that race is safe.
        Volatile.Write(ref s_moduleHandle, moduleHandle);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleEx(uint flags, nint address, out nint module);

    [StructLayout(LayoutKind.Sequential)]
    private struct ModuleInformation
    {
        public nint FileName;
        public nint BaseAddress;
        public nint SymbolName;
        public nint SymbolAddress;
    }
}

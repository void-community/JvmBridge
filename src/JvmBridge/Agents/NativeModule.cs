using System.Runtime.InteropServices;

namespace JvmBridge.Agents;

/// <summary>Retains the NativeAOT module when a JVM releases its agent-library handle.</summary>
internal static unsafe partial class NativeModule
{
    private static readonly object _sync = new();
    private static bool _retained;

    internal static void Retain(nint entryPoint)
    {
        lock (_sync)
        {
            if (_retained)
                return;
            if (OperatingSystem.IsWindows())
            {
                // FROM_ADDRESS | PIN: the NativeAOT runtime cannot be unloaded.
                if (!GetModuleHandleEx(5, entryPoint, out _))
                    throw new InvalidOperationException($"Unable to pin agent module: {Marshal.GetLastPInvokeError()}.");
            }
            else
            {
                var resolve = (delegate* unmanaged<nint, ModuleInformation*, int>)NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), "dladdr");
                ModuleInformation information = default;
                if (resolve(entryPoint, &information) == 0 || information.FileName == 0)
                    throw new InvalidOperationException("Unable to identify the native agent module.");
                string path = Marshal.PtrToStringUTF8(information.FileName) ?? throw new InvalidOperationException("Native module path is unavailable.");
                // Retain an independent dlopen reference for process lifetime.
                // OpenJ9 may dlclose its own handle during DestroyJavaVM.
                _ = NativeLibrary.Load(path);
            }
            _retained = true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ModuleInformation
    {
        public nint FileName;
        public nint BaseAddress;
        public nint SymbolName;
        public nint SymbolAddress;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleEx(uint flags, nint address, out nint module);
}

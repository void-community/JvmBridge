using System.Runtime.InteropServices;
using System.Text;

namespace JvmBridge.Runtime;

internal static unsafe class NativeJvmLibrary
{
    internal static nint Load(string path)
    {
        if (OperatingSystem.IsWindows())
            return NativeLibrary.Load(path);
        // JVM support libraries resolve symbols from libjvm. RTLD_LOCAL breaks
        // this dependency relationship on musl and some JVM distributions.
        var open = (delegate* unmanaged<byte*, int, nint>)NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), "dlopen");
        int flags = 2 | (OperatingSystem.IsMacOS() ? 8 : 0x100); // RTLD_NOW | RTLD_GLOBAL
        fixed (byte* encoded = Encoding.UTF8.GetBytes(path + '\0'))
        {
            nint handle = open(encoded, flags);
            if (handle == 0)
                throw new DllNotFoundException($"Unable to load JVM library '{path}' with global symbol visibility.");
            return handle;
        }
    }
}

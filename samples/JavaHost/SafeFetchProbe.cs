internal static unsafe class SafeFetchProbe
{
    private static nint s_library;

    internal static int Count()
    {
        delegate* unmanaged[Cdecl]<int> count = (delegate* unmanaged[Cdecl]<int>)System.Runtime.InteropServices.NativeLibrary.GetExport(s_library, name: "JvmBridgeDiagnosticCount");

        return count();
    }

    internal static void Install(string jvmPath)
    {
        s_library = System.Runtime.InteropServices.NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, path2: "safefetch-probe.dll"));
        delegate* unmanaged[Cdecl]<nint, int> install = (delegate* unmanaged[Cdecl]<nint, int>)System.Runtime.InteropServices.NativeLibrary.GetExport(s_library, name: "JvmBridgeDiagnosticInstall");

        if (install(System.Runtime.InteropServices.NativeLibrary.Load(jvmPath)) == 0)
            throw new InvalidOperationException(message: "Could not install diagnostic recovery.");
    }
}

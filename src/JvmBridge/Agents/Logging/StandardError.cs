using System.Runtime.InteropServices;
using System.Text;

namespace JvmBridge.Agents;

/// <summary>
/// Writes lines to standard error without <see cref="Console"/>. On Unix the console initializes terminal and globalization state, which aborts the process when libicu is missing,
/// so the descriptor is written directly. Writing never throws.
/// </summary>
internal static unsafe class StandardError
{
    private const int StandardErrorDescriptor = 2;

    private const int Attempts = 8;

    private static nint s_write;

    internal static void WriteLine(string text)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text + Environment.NewLine);

            if (OperatingSystem.IsWindows())
            {
                // Windows does not depend on libicu, and its console handles need the console API.
                using Stream console = Console.OpenStandardError();
                console.Write(bytes);
                console.Flush();

                return;
            }

            Write(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException or ArgumentException or EntryPointNotFoundException)
        {
            // Logging must never fail the callback that asked for it.
        }
    }

    // write(2) moves the offset that the descriptor shares with the JVM and every other writer, and appends when the file was opened to append. A FileStream over the
    // descriptor writes a regular file at a position it tracks itself instead, so the JVM's output and the next line overwrote the agent's lines when standard error was a file.
    private static void Write(byte[] bytes)
    {
        nint address = Volatile.Read(ref s_write);

        if (address == 0)
        {
            address = NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), name: "write");
            Volatile.Write(ref s_write, address);
        }

        delegate* unmanaged<int, byte*, nuint, nint> write = (delegate* unmanaged<int, byte*, nuint, nint>)address;
        int offset = 0;
        int failures = 0;

        fixed (byte* start = bytes)
        {
            // A write can be partial, or interrupted by a signal; a descriptor that keeps failing is given up on, since logging must not block the JVM.
            while (offset < bytes.Length && failures < Attempts)
            {
                nint written = write(StandardErrorDescriptor, start + offset, (nuint)(bytes.Length - offset));

                if (written > 0)
                    offset += (int)written;
                else
                    failures++;
            }
        }
    }
}

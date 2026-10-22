using System.Text;

using Microsoft.Win32.SafeHandles;

namespace JvmBridge.Agents;

/// <summary>
/// Writes lines to standard error without <see cref="Console"/>. On Unix the console initializes terminal and globalization state, which aborts the process when libicu is missing,
/// so the descriptor is written directly. Writing never throws.
/// </summary>
internal static class StandardError
{
    private const int StandardErrorDescriptor = 2;

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

            using SafeFileHandle handle = new(StandardErrorDescriptor, ownsHandle: false);
            using FileStream stream = new(handle, FileAccess.Write, bufferSize: 0);
            stream.Write(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException or ArgumentException)
        {
            // Logging must never fail the callback that asked for it.
        }
    }
}

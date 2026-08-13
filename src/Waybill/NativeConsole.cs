using System.Runtime.InteropServices;

namespace Waybill;

/// <summary>
/// The app is built as a WinExe so double-clicking it shows only the window.
/// That also means it has no console of its own, so CLI output would be thrown
/// away when run from a terminal. Attaching to the parent process's console
/// puts that output back where the user typed the command.
/// </summary>
internal static class NativeConsole {
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    public static void AttachToParent() {
        // When stdout is already redirected - piped, or sent to a file - it is a real
        // handle the caller is reading, and attaching a console over the top of it
        // would send the output to a screen buffer nobody is capturing instead.
        if (Console.IsOutputRedirected) return;

        if (!AttachConsole(AttachParentProcess)) return;

        // Console.Out was bound to the null device when the process started without a
        // console; rebind it to the console just attached.
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(stdout);
        Console.SetError(stdout);
        Console.WriteLine();
    }
}

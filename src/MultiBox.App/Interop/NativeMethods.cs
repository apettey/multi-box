using System.Runtime.InteropServices;
using System.Text;

namespace MultiBox.App.Interop;

/// <summary>
/// The complete native API surface of this application.
///
/// Every function here is a read-only query about window geometry and titles. There is
/// deliberately no API in this file that can modify, move, focus or send anything to
/// another process's window, and none that can read another process's memory or capture
/// the screen. That is an EULA requirement, not a style choice.
///
/// Do not add to this list without checking it against docs/EULA-COMPLIANCE.md. In
/// particular, SetWindowPos, FindWindow, SetForegroundWindow, PostMessage, SendMessage,
/// SendInput and any BitBlt/PrintWindow screen-capture call must stay out.
/// </summary>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Used only to render a highlight on the tile of whichever client is in front. This
    /// reads which window has focus; it cannot change it.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
}

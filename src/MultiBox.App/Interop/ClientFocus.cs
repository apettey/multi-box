using System.Runtime.InteropServices;

namespace MultiBox.App.Interop;

/// <summary>
/// Brings an EVE client window to the foreground when its thumbnail is clicked.
///
/// This is the one place in the application that acts on a game window rather than merely
/// observing one, and it is a deliberate, documented exception rather than an oversight.
/// It does exactly what eve-o-preview does when you click a thumbnail: the real client is
/// restored if minimised and raised to the foreground, after which Windows delivers your
/// keyboard and mouse to the game as normal.
///
/// What it still does not do, and must not grow into: it sends no input, reads no memory,
/// captures no pixels, and never targets more than the single window you clicked. Input
/// broadcasting - one keypress reaching several clients - is the thing EVE's EULA actually
/// prohibits, and nothing here can express it.
///
/// See docs/EULA-COMPLIANCE.md.
/// </summary>
internal static class ClientFocus
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    /// <summary>
    /// Raises one window. Returns false when Windows declined, which it may legitimately do
    /// under its foreground-lock rules if the app is not the active window; there is no way
    /// to force it and no attempt is made to.
    /// </summary>
    public static bool Activate(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return false;

        // Restoring first matters: SetForegroundWindow on a minimised window raises it
        // without unminimising, leaving the player looking at an empty desktop.
        if (IsIconic(handle))
            ShowWindow(handle, SwRestore);

        return SetForegroundWindow(handle);
    }
}

using System.Text;
using MultiBox.Core.Config;
using MultiBox.Core.Model;

namespace MultiBox.App.Interop;

/// <summary>One running EVE client window.</summary>
public sealed record EveClientWindow(IntPtr Handle, string Title, string CharacterName, ClientLayout Layout)
{
    public bool IsForeground => NativeMethods.GetForegroundWindow() == Handle;
}

/// <summary>
/// Finds running EVE clients by window title, the same way eve-o-preview does: the client
/// sets its title to "EVE - &lt;Character Name&gt;" once a character is logged in, which is what
/// links a window to the pilot named in the log banner. The character-select screen shows
/// the bare title "EVE"; <see cref="FindClients"/> skips it and
/// <see cref="FindCharacterSelectClients"/> returns only those. The title rules themselves
/// live in <see cref="EveWindowTitle"/>.
/// </summary>
public static class EveClientLocator
{
    public static IReadOnlyList<EveClientWindow> FindClients()
    {
        var results = new List<EveClientWindow>();

        foreach (var (handle, title) in VisibleWindows())
        {
            if (EveWindowTitle.CharacterName(title) is not { } character)
                continue;

            if (!NativeMethods.GetWindowRect(handle, out var rect))
                continue;

            results.Add(new EveClientWindow(handle, title, character,
                new ClientLayout(rect.Left, rect.Top, rect.Width, rect.Height)));
        }

        return results;
    }

    /// <summary>
    /// Clients that are open with no character logged in: freshly started, or left at
    /// character select after logging a character off. The dashboard shows these after the
    /// character cards, so a client you logged off is not lost from view.
    /// </summary>
    public static IReadOnlyList<IntPtr> FindCharacterSelectClients() =>
        VisibleWindows()
            .Where(w => EveWindowTitle.IsCharacterSelect(w.Title))
            .Select(w => w.Handle)
            .ToList();

    /// <summary>
    /// Every open EVE client, including ones still on the character-select screen. Settings
    /// sync needs this rather than <see cref="FindClients"/>: a client sitting at character
    /// select has no character in its title but can still write settings when it closes.
    /// </summary>
    public static int CountOpenClients() =>
        VisibleWindows().Count(w => EveWindowTitle.IsClient(w.Title));

    /// <summary>Every visible top-level window that has a title, in z-order.</summary>
    private static List<(IntPtr Handle, string Title)> VisibleWindows()
    {
        var results = new List<(IntPtr, string)>();

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle))
                return true;

            var length = NativeMethods.GetWindowTextLength(handle);
            if (length == 0)
                return true;

            var buffer = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(handle, buffer, buffer.Capacity);
            results.Add((handle, buffer.ToString()));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    /// <summary>Character name of the client currently in the foreground, if any.</summary>
    public static string? ForegroundCharacter()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        return FindClients().FirstOrDefault(c => c.Handle == foreground)?.CharacterName;
    }

    /// <summary>
    /// Records each client's rectangle into the config under the same key eve-o-preview
    /// uses, so the two tools describe the same layout.
    /// </summary>
    public static void TrackLayouts(MultiBoxConfig config)
    {
        foreach (var client in FindClients())
            config.ClientLayout[client.Title] = client.Layout;
    }
}

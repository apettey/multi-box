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
/// Every EVE client from one pass over the windows: those with a character logged in, and
/// those at character select. Taken together so a client that logs in or out mid-scan is
/// counted once, on one side, rather than on both or neither.
/// </summary>
public sealed record EveClientScan(IReadOnlyList<EveClientWindow> LoggedIn, IReadOnlyList<IntPtr> CharacterSelect);

/// <summary>
/// Finds running EVE clients by window title, the same way eve-o-preview does: the client
/// sets its title to "EVE - &lt;Character Name&gt;" once a character is logged in, which is what
/// links a window to the pilot named in the log banner. The character-select screen shows
/// the bare title "EVE"; <see cref="FindClients"/> skips it and <see cref="Scan"/> returns
/// those separately. The title rules themselves
/// live in <see cref="EveWindowTitle"/>.
/// </summary>
public static class EveClientLocator
{
    public static IReadOnlyList<EveClientWindow> FindClients() => Scan().LoggedIn;

    /// <summary>
    /// Every client in one window pass. Clients at character select (freshly started, or
    /// left there after logging a character off) are kept apart: the dashboard shows them
    /// after the character cards, so a client you logged off is not lost from view.
    /// </summary>
    public static EveClientScan Scan()
    {
        var loggedIn = new List<EveClientWindow>();
        var characterSelect = new List<IntPtr>();

        foreach (var (handle, title) in VisibleWindows())
        {
            if (EveWindowTitle.IsCharacterSelect(title))
            {
                characterSelect.Add(handle);
                continue;
            }

            if (EveWindowTitle.CharacterName(title) is not { } character)
                continue;

            if (!NativeMethods.GetWindowRect(handle, out var rect))
                continue;

            loggedIn.Add(new EveClientWindow(handle, title, character,
                new ClientLayout(rect.Left, rect.Top, rect.Width, rect.Height)));
        }

        return new EveClientScan(loggedIn, characterSelect);
    }

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
    public static void TrackLayouts(MultiBoxConfig config, IEnumerable<EveClientWindow> clients)
    {
        foreach (var client in clients)
            config.ClientLayout[client.Title] = client.Layout;
    }
}

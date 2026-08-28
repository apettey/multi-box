using System.Text;
using MultiBox.Core.Config;

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
/// the bare title "EVE" and is skipped.
/// </summary>
public static class EveClientLocator
{
    private const string TitlePrefix = "EVE - ";

    public static IReadOnlyList<EveClientWindow> FindClients()
    {
        var results = new List<EveClientWindow>();

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle))
                return true;

            var length = NativeMethods.GetWindowTextLength(handle);
            if (length == 0)
                return true;

            var buffer = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(handle, buffer, buffer.Capacity);
            var title = buffer.ToString();

            if (!title.StartsWith(TitlePrefix, StringComparison.Ordinal))
                return true;

            var character = title[TitlePrefix.Length..].Trim();
            if (character.Length == 0)
                return true;

            if (!NativeMethods.GetWindowRect(handle, out var rect))
                return true;

            results.Add(new EveClientWindow(handle, title, character,
                new ClientLayout(rect.Left, rect.Top, rect.Width, rect.Height)));
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

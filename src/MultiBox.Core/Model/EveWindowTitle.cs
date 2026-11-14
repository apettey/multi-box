namespace MultiBox.Core.Model;

/// <summary>
/// What an EVE client's window title says about it. The client titles itself
/// "EVE - &lt;Character Name&gt;" while a character is logged in and plain "EVE" on the
/// character-select screen, which is also where it goes back to when a character logs off
/// without the client being closed.
///
/// Kept here rather than next to the window enumeration so the rules can be tested off
/// Windows.
/// </summary>
public static class EveWindowTitle
{
    /// <summary>The title of a client with no character logged in.</summary>
    public const string CharacterSelect = "EVE";

    private const string Prefix = "EVE - ";

    /// <summary>The logged-in character's name, or null when no character is named.</summary>
    public static string? CharacterName(string? title)
    {
        if (title is null || !title.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        var name = title[Prefix.Length..].Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>A client that is open but has no character logged in.</summary>
    public static bool IsCharacterSelect(string? title) =>
        string.Equals(title, CharacterSelect, StringComparison.Ordinal);

    /// <summary>Any EVE client window, logged in or not.</summary>
    public static bool IsClient(string? title) =>
        IsCharacterSelect(title) || (title?.StartsWith(Prefix, StringComparison.Ordinal) ?? false);
}

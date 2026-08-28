using System.Net;
using System.Text.RegularExpressions;

namespace MultiBox.Core.Parsing;

/// <summary>
/// Game-log combat lines are wrapped in pseudo-HTML (&lt;color&gt;, &lt;b&gt;, &lt;font&gt;) that the
/// client uses to colour the in-game log window. None of it carries information we need,
/// so it is removed before the line is matched.
/// </summary>
public static class EveMarkup
{
    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"[ \t]{2,}", RegexOptions.Compiled);

    public static string Strip(string line)
    {
        if (string.IsNullOrEmpty(line) || line.IndexOf('<') < 0)
            return line;

        var text = Tag.Replace(line, string.Empty);
        text = WebUtility.HtmlDecode(text);
        return Whitespace.Replace(text, " ").Trim();
    }
}

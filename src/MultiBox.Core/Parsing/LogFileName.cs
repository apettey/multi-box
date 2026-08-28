using System.Globalization;
using System.Text.RegularExpressions;

namespace MultiBox.Core.Parsing;

/// <summary>
/// Gamelogs are named "yyyyMMdd_HHmmss_&lt;characterId&gt;.txt"; chat logs prefix the channel,
/// e.g. "Fleet_20260828_162059_1899648001.txt". The trailing number is the character's ESI
/// id, which is how we group a pilot's game log and their several channel logs together
/// without any configuration.
/// </summary>
public sealed record LogFileName
{
    public required string Path { get; init; }
    public required DateTime SessionStart { get; init; }
    public required long CharacterId { get; init; }

    /// <summary>Channel name for chat logs; null for game logs.</summary>
    public string? Channel { get; init; }

    public bool IsChatLog => Channel is not null;

    private static readonly Regex Pattern = new(
        @"^(?:(?<channel>.+)_)?(?<date>\d{8})_(?<time>\d{6})_(?<charId>\d+)$",
        RegexOptions.Compiled);

    public static LogFileName? TryParse(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var m = Pattern.Match(name);
        if (!m.Success)
            return null;

        if (!DateTime.TryParseExact(m.Groups["date"].Value + m.Groups["time"].Value, "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
            return null;

        if (!long.TryParse(m.Groups["charId"].Value, out var charId))
            return null;

        return new LogFileName
        {
            Path = path,
            SessionStart = start,
            CharacterId = charId,
            Channel = m.Groups["channel"].Success ? m.Groups["channel"].Value : null
        };
    }
}

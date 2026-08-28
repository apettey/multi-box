using System.Text.RegularExpressions;

namespace MultiBox.Core.Parsing;

/// <summary>
/// Both log kinds start with a banner block naming the character ("Listener") and, for chat
/// logs, the channel. Reading it means we learn the pilot's name from disk and only need
/// ESI to map that name to an id, never the reverse.
/// </summary>
public sealed record LogHeader
{
    public string? Listener { get; init; }
    public string? ChannelName { get; init; }
    public string? ChannelId { get; init; }
    public DateTime? SessionStarted { get; init; }

    private static readonly Regex Field = new(
        @"^\s*(?<key>Channel ID|Channel Name|Listener|Session started|Session Started)\s*:\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled);

    /// <summary>Reads the banner from the first lines of a log file.</summary>
    public static LogHeader Parse(IEnumerable<string> lines)
    {
        string? listener = null, channelName = null, channelId = null;
        DateTime? started = null;

        foreach (var line in lines.Take(12))
        {
            var m = Field.Match(line);
            if (!m.Success)
                continue;

            var value = m.Groups["value"].Value;
            switch (m.Groups["key"].Value.ToLowerInvariant())
            {
                case "listener":
                    listener = value;
                    break;
                case "channel name":
                    channelName = value;
                    break;
                case "channel id":
                    channelId = value;
                    break;
                case "session started":
                    if (DateTime.TryParseExact(value, "yyyy.MM.dd HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal |
                            System.Globalization.DateTimeStyles.AdjustToUniversal, out var ts))
                        started = ts;
                    break;
            }
        }

        return new LogHeader
        {
            Listener = listener,
            ChannelName = channelName,
            ChannelId = channelId,
            SessionStarted = started
        };
    }
}

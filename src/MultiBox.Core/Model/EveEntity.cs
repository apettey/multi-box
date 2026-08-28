using System.Text.RegularExpressions;

namespace MultiBox.Core.Model;

/// <summary>
/// A participant in a combat line. Player entities are logged as
/// "Ship // Character Name TICKER /" once markup is stripped; NPCs are a bare name.
/// </summary>
public sealed record EveEntity(string Name, string? Ship = null, string? Corporation = null)
{
    /// <summary>True when the log said "you" rather than naming the entity.</summary>
    public bool IsSelf { get; init; }

    public static readonly EveEntity Self = new("you") { IsSelf = true };

    private static readonly Regex PlayerWithTicker = new(
        @"^(?<ship>.+?)\s+//\s+(?<char>.+?)\s+(?<corp>\S+)\s*/\s*$",
        RegexOptions.Compiled);

    private static readonly Regex PlayerNoTicker = new(
        @"^(?<ship>.+?)\s+//\s+(?<char>.+?)\s*$",
        RegexOptions.Compiled);

    /// <summary>Parses an already-markup-stripped entity fragment.</summary>
    public static EveEntity Parse(string fragment)
    {
        var text = fragment.Trim();
        if (text.Length == 0)
            return new EveEntity(string.Empty);

        if (text.Equals("you", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("you!", StringComparison.OrdinalIgnoreCase))
            return Self;

        var m = PlayerWithTicker.Match(text);
        if (m.Success)
            return new EveEntity(m.Groups["char"].Value.Trim(), m.Groups["ship"].Value.Trim(), m.Groups["corp"].Value.Trim());

        m = PlayerNoTicker.Match(text);
        if (m.Success)
            return new EveEntity(m.Groups["char"].Value.Trim(), m.Groups["ship"].Value.Trim());

        return new EveEntity(text);
    }

    /// <summary>Resolves "you" to the character who owns the log file being read.</summary>
    public EveEntity ResolveSelf(string listener) => IsSelf ? this with { Name = listener } : this;

    public override string ToString() => Ship is null ? Name : $"{Name} ({Ship})";
}

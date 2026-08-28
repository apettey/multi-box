using System.Text.RegularExpressions;
using MultiBox.Core.Model;

namespace MultiBox.Core.Parsing;

/// <summary>
/// Parses lines of an EVE Gamelog file into <see cref="GameLogEvent"/>s.
///
/// Every pattern here was derived from real logs in samples/Gamelogs. The important
/// asymmetry: the client writes combat lines from the reader's point of view, so the same
/// event reads "to you!" in the victim's file and "to Ship // Name TICKER /" in everyone
/// else's. <see cref="EveEntity.ResolveSelf"/> normalises that away.
/// </summary>
public sealed class GamelogParser
{
    // [ 2026.08.28 16:23:52 ] (combat) ...
    private static readonly Regex LinePrefix = new(
        @"^\[ (?<ts>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}) \] \((?<cat>\w+)\) (?<body>.*)$",
        RegexOptions.Compiled);

    // "36 from Anchoring Damavik - Hits"  /  "295 to Sparkneedle Tessella - Small Focused Pulse Laser II - Penetrates"
    private static readonly Regex Damage = new(
        @"^(?<amount>\d+)\s+(?<dir>to|from)\s+(?<entity>.+?)(?:\s+-\s+(?<tail>.+))?$",
        RegexOptions.Compiled);

    // "103 remote armor repaired by Deacon // Major Tyrael VI.TA / - Coreli A-Type Small Remote Armor Repairer"
    // Module names legitimately contain hyphens ("Coreli A-Type ..."), so the separator is
    // " - " with spaces and the entity is whatever precedes the first one.
    private static readonly Regex RemoteRep = new(
        @"^(?<amount>\d+)\s+remote (?<layer>armor|shield|hull) repaired\s+(?<dir>to|by)\s+(?<entity>.+?)(?:\s+-\s+(?<module>.+))?$",
        RegexOptions.Compiled);

    // "66 GJ energy neutralized Starving Damavik - Starving Damavik"
    private static readonly Regex Neut = new(
        @"^(?<amount>\d+)\s+GJ energy neutralized\s+(?<by>by\s+)?(?<entity>.+?)(?:\s+-\s+(?<module>.+))?$",
        RegexOptions.Compiled);

    // "Warp scramble attempt from Anchoring Damavik to you!"
    // "Warp disruption attempt from Garmur // Commander Tyrael VI.TA / to you!"
    private static readonly Regex TackleAttempt = new(
        @"^Warp (?<kind>scramble|disruption) attempt\s+from\s+(?<source>.+?)\s+to\s+(?<target>.+?)!?$",
        RegexOptions.Compiled);

    // "You're jammed by Rook // Lieutent Tyrael VI.TA / - Radar ECM II"
    private static readonly Regex JammedIncoming = new(
        @"^You're jammed by\s+(?<source>.+?)(?:\s+-\s+(?<module>.+))?$",
        RegexOptions.Compiled);

    // "Garmur // Commander Tyrael VI.TA / jammed - Radar ECM II"
    private static readonly Regex JammedOutgoing = new(
        @"^(?<target>.+?)\s+jammed(?:\s+-\s+(?<module>.+))?$",
        RegexOptions.Compiled);

    private static readonly Regex MissesYou = new(
        @"^(?<source>.+?)\s+misses you completely$",
        RegexOptions.Compiled);

    // "Your group of Small Focused Pulse Laser II misses Ghosting Damavik completely - <module>"
    private static readonly Regex MissesTarget = new(
        @"^Your (?:group of )?(?<module>.+?) misses (?<target>.+?) completely(?:\s+-\s+(?<tail>.+))?$",
        RegexOptions.Compiled);

    private readonly string _listener;

    public GamelogParser(string listener) => _listener = listener;

    public GameLogEvent? ParseLine(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
            return null;

        var prefix = LinePrefix.Match(rawLine);
        if (!prefix.Success)
            return null;

        if (!DateTime.TryParseExact(prefix.Groups["ts"].Value, "yyyy.MM.dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            return null;

        // Only (combat) lines carry the events we track. (notify) is UI chatter such as
        // "X is too far away to use your Stasis Webifier on" - see WebifierNotice below.
        if (!prefix.Groups["cat"].Value.Equals("combat", StringComparison.OrdinalIgnoreCase))
            return null;

        var body = EveMarkup.Strip(prefix.Groups["body"].Value);
        if (body.Length == 0)
            return null;

        return ParseCombatBody(timestamp, body, rawLine);
    }

    private GameLogEvent? ParseCombatBody(DateTime timestamp, string body, string rawLine)
    {
        GameLogEvent Base(CombatEventKind kind, Direction dir) => new()
        {
            Timestamp = timestamp,
            Kind = kind,
            Direction = dir,
            Listener = _listener,
            RawLine = rawLine
        };

        var m = TackleAttempt.Match(body);
        if (m.Success)
        {
            var ewar = m.Groups["kind"].Value.Equals("scramble", StringComparison.OrdinalIgnoreCase)
                ? EwarType.WarpScramble
                : EwarType.WarpDisruption;
            var source = EveEntity.Parse(m.Groups["source"].Value).ResolveSelf(_listener);
            var target = EveEntity.Parse(m.Groups["target"].Value).ResolveSelf(_listener);
            var outgoing = source.Name.Equals(_listener, StringComparison.OrdinalIgnoreCase);

            return Base(CombatEventKind.Ewar, outgoing ? Direction.Outgoing : Direction.Incoming) with
            {
                Ewar = ewar,
                Counterparty = outgoing ? target : source,
                Victim = target.Name,
                VictimShip = target.Ship
            };
        }

        m = JammedIncoming.Match(body);
        if (m.Success)
        {
            var source = EveEntity.Parse(m.Groups["source"].Value).ResolveSelf(_listener);
            return Base(CombatEventKind.Ewar, Direction.Incoming) with
            {
                Ewar = EwarType.Jam,
                Counterparty = source,
                Victim = _listener,
                Module = Trim(m.Groups["module"].Value)
            };
        }

        m = RemoteRep.Match(body);
        if (m.Success)
        {
            var entity = EveEntity.Parse(m.Groups["entity"].Value).ResolveSelf(_listener);
            // "repaired by X" = X repaired us; "repaired to X" = we repaired X.
            var incoming = m.Groups["dir"].Value.Equals("by", StringComparison.OrdinalIgnoreCase);
            return Base(CombatEventKind.RemoteRepair, incoming ? Direction.Incoming : Direction.Outgoing) with
            {
                Amount = int.Parse(m.Groups["amount"].Value),
                Counterparty = entity,
                Victim = incoming ? _listener : entity.Name,
                VictimShip = incoming ? null : entity.Ship,
                Module = Trim(m.Groups["module"].Value)
            };
        }

        m = Neut.Match(body);
        if (m.Success)
        {
            var entity = EveEntity.Parse(m.Groups["entity"].Value).ResolveSelf(_listener);
            var incoming = m.Groups["by"].Success;
            return Base(CombatEventKind.EnergyNeutralized, incoming ? Direction.Incoming : Direction.Outgoing) with
            {
                Amount = int.Parse(m.Groups["amount"].Value),
                Counterparty = entity,
                Victim = incoming ? _listener : entity.Name,
                Ewar = incoming ? EwarType.EnergyNeutralizer : null,
                Module = Trim(m.Groups["module"].Value)
            };
        }

        m = Damage.Match(body);
        if (m.Success)
        {
            var entity = EveEntity.Parse(m.Groups["entity"].Value).ResolveSelf(_listener);
            var incoming = m.Groups["dir"].Value.Equals("from", StringComparison.OrdinalIgnoreCase);
            var (module, quality) = SplitTail(m.Groups["tail"].Value);
            return Base(CombatEventKind.Damage, incoming ? Direction.Incoming : Direction.Outgoing) with
            {
                Amount = int.Parse(m.Groups["amount"].Value),
                Counterparty = entity,
                Victim = incoming ? _listener : entity.Name,
                Module = module,
                Quality = quality
            };
        }

        m = MissesYou.Match(body);
        if (m.Success)
        {
            return Base(CombatEventKind.Miss, Direction.Incoming) with
            {
                Counterparty = EveEntity.Parse(m.Groups["source"].Value),
                Victim = _listener
            };
        }

        m = MissesTarget.Match(body);
        if (m.Success)
        {
            var target = EveEntity.Parse(m.Groups["target"].Value).ResolveSelf(_listener);
            return Base(CombatEventKind.Miss, Direction.Outgoing) with
            {
                Counterparty = target,
                Victim = target.Name,
                Module = Trim(m.Groups["module"].Value)
            };
        }

        // Checked last: "<target> jammed - <module>" is broad enough to swallow other
        // shapes, so every more specific pattern gets first refusal.
        m = JammedOutgoing.Match(body);
        if (m.Success && !body.StartsWith("You're", StringComparison.OrdinalIgnoreCase))
        {
            var target = EveEntity.Parse(m.Groups["target"].Value).ResolveSelf(_listener);
            return Base(CombatEventKind.Ewar, Direction.Outgoing) with
            {
                Ewar = EwarType.Jam,
                Counterparty = target,
                Victim = target.Name,
                VictimShip = target.Ship,
                Module = Trim(m.Groups["module"].Value)
            };
        }

        return null;
    }

    /// <summary>
    /// Damage tails are either "Hits" or "Module - Hits". Splitting on the last separator
    /// keeps module names containing hyphens (e.g. "Coreli A-Type ...") intact.
    /// </summary>
    private static (string? Module, string? Quality) SplitTail(string tail)
    {
        tail = tail.Trim();
        if (tail.Length == 0)
            return (null, null);

        var idx = tail.LastIndexOf(" - ", StringComparison.Ordinal);
        return idx < 0 ? (null, tail) : (tail[..idx].Trim(), tail[(idx + 3)..].Trim());
    }

    private static string? Trim(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

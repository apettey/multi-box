namespace MultiBox.Core.Model;

/// <summary>A single parsed line from a character's game log.</summary>
public sealed record GameLogEvent
{
    public required DateTime Timestamp { get; init; }
    public required CombatEventKind Kind { get; init; }
    public required Direction Direction { get; init; }

    /// <summary>Character whose log file this line came from.</summary>
    public required string Listener { get; init; }

    /// <summary>Damage / repair amount / GJ neutralized. Zero for EWAR and misses.</summary>
    public int Amount { get; init; }

    /// <summary>The other party: attacker for incoming, target for outgoing.</summary>
    public EveEntity? Counterparty { get; init; }

    /// <summary>
    /// Who the effect landed on. For incoming events this is the listener; for events the
    /// listener merely witnessed (fleet-mate scrammed) it is that fleet-mate.
    /// </summary>
    public string? Victim { get; init; }

    /// <summary>
    /// Ship the victim was flying, when the line named it. Only witness lines from another
    /// client carry this - your own log says "you" rather than naming your ship.
    /// </summary>
    public string? VictimShip { get; init; }

    public EwarType? Ewar { get; init; }
    public string? Module { get; init; }
    public string? Quality { get; init; }
    public string RawLine { get; init; } = string.Empty;

    /// <summary>
    /// Identity of the underlying game event, independent of which client observed it.
    /// Used to collapse the same scramble seen by four clients into one alert.
    /// </summary>
    public string DedupKey() =>
        $"{Timestamp:yyyyMMddHHmmss}|{Kind}|{Ewar}|{Victim}|{Counterparty?.Name}|{Amount}|{Module}";
}

/// <summary>A chat line from one character's copy of a channel log.</summary>
public sealed record ChatMessage
{
    public required DateTime Timestamp { get; init; }
    public required string Channel { get; init; }
    public required string Sender { get; init; }
    public required string Text { get; init; }

    /// <summary>Character whose chat log file this line came from.</summary>
    public required string Listener { get; init; }

    /// <summary>True for "EVE System &gt; Channel changed to ..." style lines.</summary>
    public bool IsSystem => Sender.Equals("EVE System", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Identity of the message itself. Four clients in the same fleet channel log the
    /// identical timestamp, sender and body, so this key collapses them to one row.
    /// </summary>
    public string DedupKey() => $"{Timestamp:yyyyMMddHHmmss}|{Channel}|{Sender}|{Text}";
}

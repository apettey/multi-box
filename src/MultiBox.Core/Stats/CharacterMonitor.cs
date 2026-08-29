using MultiBox.Core.Model;

namespace MultiBox.Core.Stats;

/// <summary>Live state for one pilot: the tile the UI renders per character.</summary>
public sealed class CharacterMonitor
{
    public CharacterMonitor(string name, long characterId, TimeSpan? window = null, TimeSpan? ewarHold = null)
    {
        Name = name;
        CharacterId = characterId;
        var w = window ?? TimeSpan.FromSeconds(10);
        DamageOut = new RollingWindow(w);
        DamageIn = new RollingWindow(w);
        RepsOut = new RollingWindow(w);
        RepsIn = new RollingWindow(w);
        NeutIn = new RollingWindow(w);
        CapTransferIn = new RollingWindow(w);
        Threats = new ThreatTracker(w);
        Ewar = new EwarStateTracker(ewarHold);
    }

    public string Name { get; }
    public long CharacterId { get; }
    public string? Ship { get; private set; }

    public RollingWindow DamageOut { get; }
    public RollingWindow DamageIn { get; }
    public RollingWindow RepsOut { get; }
    public RollingWindow RepsIn { get; }
    public RollingWindow NeutIn { get; }

    /// <summary>Remote capacitor received. Unverified against captured logs - see GamelogParser.</summary>
    public RollingWindow CapTransferIn { get; }

    /// <summary>
    /// Incoming damage split by who is dealing it. The headline figure says how much is
    /// landing; this says what to shoot, burn away from, or ask for reps against.
    /// </summary>
    public ThreatTracker Threats { get; }

    public EwarStateTracker Ewar { get; }

    /// <summary>Trailing samples behind the incoming-damage sparkline.</summary>
    public MetricHistory DamageInHistory { get; } = new();

    /// <summary>Trailing samples behind the reps-in sparkline, drawn against the same axis.</summary>
    public MetricHistory RepsInHistory { get; } = new();

    /// <summary>Recent lines about this character, newest first.</summary>
    public CombatLogBuffer CombatLog { get; } = new();

    public DateTime LastEventAt { get; private set; }

    /// <summary>Wall-clock instant at which <see cref="LastEventAt"/> was actually read.</summary>
    public DateTime LastEventObservedAt { get; private set; }

    /// <summary>
    /// The log's own clock, carried forward to wall-clock now.
    ///
    /// EVE does not flush its gamelog as it fights; it buffers and writes in bursts, so a
    /// line describing something that happened half a minute ago can reach us now. Reading a
    /// ten-second rolling window at <see cref="DateTime.UtcNow"/> therefore discards every
    /// sample the instant it arrives whenever that flush lag exceeds the window — the numbers
    /// sit at zero through an entire fight while the combat log, which has no window, fills
    /// up normally.
    ///
    /// Anchoring to the newest event's own timestamp and advancing it by however long we have
    /// been waiting since keeps the window aligned with the data, and still lets the readings
    /// decay to zero once the shooting actually stops.
    /// </summary>
    public DateTime ProjectedNow(DateTime wallClockNow) =>
        LastEventAt == default
            ? wallClockNow
            : LastEventAt + (wallClockNow - LastEventObservedAt);

    /// <summary>
    /// Advances both sparklines by one sample. Called on a timer rather than per event so a
    /// quiet second occupies the same width as a busy one.
    /// </summary>
    public void SampleHistory(DateTime now)
    {
        DamageInHistory.Add(DamageIn.PerSecond(now));
        RepsInHistory.Add(RepsIn.PerSecond(now));
    }

    /// <summary>
    /// Applies one parsed event. Only events whose victim is this character update its
    /// incoming stats, so a fleet-mate's scramble seen in this log does not light up
    /// this pilot's indicators.
    /// </summary>
    /// <param name="observedAt">
    /// Wall-clock instant this line was read, which is later than the event's own timestamp by
    /// however long EVE sat on it. Defaults to the event's timestamp, so replaying a captured
    /// log produces identical numbers to the live session.
    /// </param>
    public void Apply(GameLogEvent e, DateTime? observedAt = null)
    {
        // The session broadcasts every event to every monitor, so each one must filter.
        // An outgoing event is only ever written to the acting pilot's own log, which makes
        // the log's owner the actor. An incoming event names its victim explicitly and may
        // arrive via any client that witnessed it.
        var isMyAction = e.Listener.Equals(Name, StringComparison.OrdinalIgnoreCase);
        var aboutMe = e.Victim is not null && e.Victim.Equals(Name, StringComparison.OrdinalIgnoreCase);

        if (!isMyAction && !aboutMe)
            return;

        if (e.Timestamp > LastEventAt)
        {
            LastEventAt = e.Timestamp;
            LastEventObservedAt = observedAt ?? e.Timestamp;
        }

        // Another client witnessing an effect on me also records which ship I am flying.
        if (aboutMe && e.VictimShip is not null)
            Ship = e.VictimShip;

        switch (e.Kind)
        {
            case CombatEventKind.Damage:
                if (e.Direction == Direction.Outgoing)
                {
                    if (isMyAction)
                        DamageOut.Add(e.Timestamp, e.Amount);
                }
                else if (aboutMe)
                {
                    DamageIn.Add(e.Timestamp, e.Amount);
                    Threats.Add(e.Timestamp, e.Counterparty, e.Amount);
                }
                break;

            case CombatEventKind.RemoteRepair:
                if (e.Direction == Direction.Outgoing)
                {
                    if (isMyAction)
                        RepsOut.Add(e.Timestamp, e.Amount);
                }
                else if (aboutMe)
                {
                    RepsIn.Add(e.Timestamp, e.Amount);
                }
                break;

            case CombatEventKind.EnergyNeutralized:
                if (e.Direction == Direction.Incoming && aboutMe)
                {
                    NeutIn.Add(e.Timestamp, e.Amount);
                    Ewar.Apply(EwarType.EnergyNeutralizer, e.Timestamp, e.Counterparty?.Name, e.Module,
                        e.Counterparty?.Ship);
                }
                break;

            case CombatEventKind.CapacitorTransfer:
                if (e.Direction == Direction.Incoming && aboutMe)
                    CapTransferIn.Add(e.Timestamp, e.Amount);
                break;

            case CombatEventKind.Ewar:
                if (e.Direction == Direction.Incoming && aboutMe && e.Ewar is { } type)
                    Ewar.Apply(type, e.Timestamp, e.Counterparty?.Name, e.Module, e.Counterparty?.Ship);
                break;
        }

        if (aboutMe)
            RecordLogLine(e);
    }

    /// <summary>
    /// Turns an event into the one-line form the card shows. Only events landing on this
    /// character are recorded: the card answers "what is happening to me", and its own
    /// outgoing damage already has a dedicated readout.
    /// </summary>
    private void RecordLogLine(GameLogEvent e)
    {
        if (e.Direction != Direction.Incoming)
            return;

        var who = e.Counterparty?.Ship ?? e.Counterparty?.Name;
        var from = who is null ? string.Empty : $" from {who}";
        var by = who is null ? string.Empty : $" by {who}";

        var (text, kind) = e.Kind switch
        {
            CombatEventKind.Damage =>
                ($"{e.Quality ?? "Hits"} {e.Amount}{from}",
                 e.Amount >= HeavyHitThreshold ? CombatLogKind.DamageHeavy : CombatLogKind.DamageLight),

            CombatEventKind.RemoteRepair => ($"+{e.Amount} rep{from}", CombatLogKind.Reps),
            CombatEventKind.CapacitorTransfer => ($"+{e.Amount} cap{from}", CombatLogKind.Cap),
            CombatEventKind.EnergyNeutralized => ($"-{e.Amount} GJ neut{from}", CombatLogKind.Neut),

            CombatEventKind.Ewar when e.Ewar is { } type =>
                ($"{EwarTypeInfo.DisplayName(type)}{by}", CombatLogKind.Ewar),

            _ => (string.Empty, CombatLogKind.Idle)
        };

        if (text.Length > 0)
            CombatLog.Add(new CombatLogEntry(e.Timestamp, text, kind));
    }

    /// <summary>
    /// Above this, a hit is drawn in full red. Below it, muted. The split exists so a wall of
    /// chip damage does not look the same as the volley that actually threatens the ship.
    /// </summary>
    private const int HeavyHitThreshold = 200;

    public void SetShip(string? ship) => Ship = ship;
}

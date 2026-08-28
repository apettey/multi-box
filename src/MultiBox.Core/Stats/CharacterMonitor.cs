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
    public EwarStateTracker Ewar { get; }

    public DateTime LastEventAt { get; private set; }

    /// <summary>
    /// Applies one parsed event. Only events whose victim is this character update its
    /// incoming stats, so a fleet-mate's scramble seen in this log does not light up
    /// this pilot's indicators.
    /// </summary>
    public void Apply(GameLogEvent e)
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
            LastEventAt = e.Timestamp;

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
                    Ewar.Apply(EwarType.EnergyNeutralizer, e.Timestamp, e.Counterparty?.Name, e.Module);
                }
                break;

            case CombatEventKind.Ewar:
                if (e.Direction == Direction.Incoming && aboutMe && e.Ewar is { } type)
                    Ewar.Apply(type, e.Timestamp, e.Counterparty?.Name, e.Module);
                break;
        }

    }

    public void SetShip(string? ship) => Ship = ship;
}

namespace MultiBox.Core.Model;

/// <summary>Direction of an event relative to the character whose log produced it.</summary>
public enum Direction
{
    Incoming,
    Outgoing
}

public enum CombatEventKind
{
    Damage,
    RemoteRepair,
    EnergyNeutralized,
    CapacitorTransfer,
    Ewar,
    Miss
}

/// <summary>
/// How a combat-log row reads at a glance. Damage splits by size because a card showing
/// twenty rows of identical red tells you nothing about which hit mattered.
/// </summary>
public enum CombatLogKind
{
    DamageHeavy,
    DamageLight,
    Reps,
    Cap,
    Neut,
    Ewar,
    Idle
}

/// <summary>
/// Electronic warfare effects. Only the values flagged by <see cref="EwarTypeInfo.IsLogged"/>
/// can actually be observed from the game log; the rest exist so the UI and config have a
/// stable slot for them if CCP ever starts emitting a line.
/// </summary>
public enum EwarType
{
    Jam,
    WarpScramble,
    WarpDisruption,
    EnergyNeutralizer,
    Web,
    TargetPainter,
    SensorDampener,
    TrackingDisruptor
}

public static class EwarTypeInfo
{
    /// <summary>
    /// True when the EVE client writes a game-log line for this effect being applied.
    /// Webs, painters, dampeners and tracking disruptors produce no log line at all,
    /// so no amount of parsing can surface them.
    /// </summary>
    public static bool IsLogged(EwarType type) => type switch
    {
        EwarType.Jam => true,
        EwarType.WarpScramble => true,
        EwarType.WarpDisruption => true,
        EwarType.EnergyNeutralizer => true,
        _ => false
    };

    public static string DisplayName(EwarType type) => type switch
    {
        EwarType.Jam => "Jammed",
        EwarType.WarpScramble => "Scrambled",
        EwarType.WarpDisruption => "Disrupted",
        EwarType.EnergyNeutralizer => "Neuted",
        EwarType.Web => "Webbed",
        EwarType.TargetPainter => "Painted",
        EwarType.SensorDampener => "Damped",
        EwarType.TrackingDisruptor => "Tracking Disrupted",
        _ => type.ToString()
    };
}

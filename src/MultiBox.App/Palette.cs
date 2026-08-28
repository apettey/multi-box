using System.Windows.Media;
using MultiBox.Core.Model;
using MultiBox.Core.Stats;

namespace MultiBox.App;

/// <summary>
/// The Fleet Command design tokens, in one place.
///
/// These live in code rather than a XAML resource dictionary because half the palette is
/// chosen at runtime — a log row's colour depends on what kind of event it was, an EWAR
/// badge's on which effect landed — and a single source of truth beats a resource dictionary
/// that code-behind has to look strings up in.
/// </summary>
public static class Palette
{
    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    // Surfaces
    public static readonly SolidColorBrush Page = Frozen("#0a0d11");
    public static readonly SolidColorBrush Header = Frozen("#0d1117");
    public static readonly SolidColorBrush Panel = Frozen("#10151b");
    public static readonly SolidColorBrush Inset = Frozen("#1a212a");
    public static readonly SolidColorBrush Chrome = Frozen("#131920");
    public static readonly SolidColorBrush HatchDark = Frozen("#0c1015");
    public static readonly SolidColorBrush HatchLight = Frozen("#0e1319");

    // Borders
    public static readonly SolidColorBrush Divider = Frozen("#1d242e");
    public static readonly SolidColorBrush CardBorder = Frozen("#232b35");
    public static readonly SolidColorBrush ActiveBorder = Frozen("#3a4550");
    public static readonly SolidColorBrush ThreatBorder = Frozen("#ff5c5c88");
    public static readonly SolidColorBrush LogSeparator = Frozen("#1a212a");

    // Text
    public static readonly SolidColorBrush TextPrimary = Frozen("#e8eef5");
    public static readonly SolidColorBrush TextBody = Frozen("#cfd8e3");
    public static readonly SolidColorBrush TextSecondary = Frozen("#8a96a3");
    public static readonly SolidColorBrush TextMuted = Frozen("#5c6875");
    public static readonly SolidColorBrush TextFaint = Frozen("#48545f");
    public static readonly SolidColorBrush TextDisabled = Frozen("#3a4550");

    // Semantic
    public static readonly SolidColorBrush Danger = Frozen("#ff5c5c");
    public static readonly SolidColorBrush DamageLight = Frozen("#c9a0a0");
    public static readonly SolidColorBrush Success = Frozen("#4cc38a");
    public static readonly SolidColorBrush DpsOut = Frozen("#ffb454");
    public static readonly SolidColorBrush Cap = Frozen("#58a6ff");
    public static readonly SolidColorBrush NeutBlue = Frozen("#4dabf7");
    public static readonly SolidColorBrush Gold = Frozen("#d0a04a");
    public static readonly SolidColorBrush Hover = Frozen("#4dabf7");

    // EWAR badge colours
    public static readonly SolidColorBrush EwarJam = Frozen("#e864ff");
    public static readonly SolidColorBrush EwarDamp = Frozen("#8f7bff");
    public static readonly SolidColorBrush EwarScram = Frozen("#ff6b6b");
    public static readonly SolidColorBrush EwarWeb = Frozen("#74c0fc");
    public static readonly SolidColorBrush EwarWebBase = Frozen("#4dabf7");
    public static readonly SolidColorBrush EwarPaint = Frozen("#ffd43b");
    public static readonly SolidColorBrush EwarNeut = Frozen("#b197fc");

    // Roles
    public static readonly SolidColorBrush RoleDps = Frozen("#ffb454");
    public static readonly SolidColorBrush RoleLogi = Frozen("#4cc38a");
    public static readonly SolidColorBrush RoleCmd = Frozen("#58a6ff");
    public static readonly SolidColorBrush RoleEwar = Frozen("#e864ff");

    // Chat channels
    public static readonly SolidColorBrush ChannelCorp = Frozen("#4cc38a");
    public static readonly SolidColorBrush ChannelAlliance = Frozen("#d0a04a");
    public static readonly SolidColorBrush ChannelLocal = Frozen("#8a96a3");
    public static readonly SolidColorBrush ChannelIntel = Frozen("#ff5c5c");

    public static SolidColorBrush ForRole(string role) => role.ToUpperInvariant() switch
    {
        "LOGI" => RoleLogi,
        "CMD" => RoleCmd,
        "EWAR" => RoleEwar,
        _ => RoleDps
    };

    public static SolidColorBrush ForEwar(EwarType type) => type switch
    {
        EwarType.Jam => EwarJam,
        EwarType.SensorDampener => EwarDamp,
        EwarType.WarpScramble => EwarScram,
        EwarType.WarpDisruption => EwarScram,
        EwarType.Web => EwarWeb,
        EwarType.TargetPainter => EwarPaint,
        EwarType.EnergyNeutralizer => EwarNeut,
        EwarType.TrackingDisruptor => EwarDamp,
        _ => EwarJam
    };

    public static SolidColorBrush ForLogKind(CombatLogKind kind) => kind switch
    {
        CombatLogKind.DamageHeavy => Danger,
        CombatLogKind.DamageLight => DamageLight,
        CombatLogKind.Reps => Success,
        CombatLogKind.Cap => Cap,
        CombatLogKind.Neut => NeutBlue,
        CombatLogKind.Ewar => EwarJam,
        _ => TextFaint
    };

    /// <summary>Channel tag colour. Anything that is not corp/alliance/local reads as intel.</summary>
    public static SolidColorBrush ForChannel(string channel) => channel.ToUpperInvariant() switch
    {
        "CORP" => ChannelCorp,
        "ALLIANCE" => ChannelAlliance,
        "LOCAL" => ChannelLocal,
        _ => ChannelIntel
    };

    /// <summary>The badge fill: the effect's own colour at roughly 10% alpha.</summary>
    public static SolidColorBrush Wash(SolidColorBrush source)
    {
        var c = source.Color;
        var brush = new SolidColorBrush(Color.FromArgb(0x1a, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>Short badge label for an effect, as the design abbreviates them.</summary>
    public static string ShortName(EwarType type) => type switch
    {
        EwarType.Jam => "JAM",
        EwarType.SensorDampener => "DAMP",
        EwarType.WarpScramble => "SCRAM",
        EwarType.WarpDisruption => "DISRUPT",
        EwarType.Web => "WEB",
        EwarType.TargetPainter => "TP",
        EwarType.EnergyNeutralizer => "NEUT",
        EwarType.TrackingDisruptor => "TD",
        _ => type.ToString().ToUpperInvariant()
    };
}

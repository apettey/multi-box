using System.Text.Json;
using System.Text.Json.Serialization;
using MultiBox.Core.Model;

namespace MultiBox.Core.Config;

public sealed record Pt(int X, int Y)
{
    public static readonly Pt Zero = new(0, 0);
}

public sealed record Sz(int Width, int Height);

/// <summary>
/// Position and size of an EVE client window, matching eve-o-preview's ClientLayout so an
/// existing layout can be imported rather than re-created by hand.
/// </summary>
public sealed record ClientLayout(int X, int Y, int Width, int Height, bool IsMaximized = false);

/// <summary>
/// Application configuration, deliberately shaped like eve-o-preview's
/// "EVE-O Preview.json": clients are keyed by their window title ("EVE - Character Name"),
/// panel positions live in a FlatLayout, and an optional PerClientLayout holds a different
/// arrangement per focused client.
/// </summary>
public sealed class MultiBoxConfig
{
    public int ConfigVersion { get; set; } = 1;

    /// <summary>Folders to watch. Empty means "use the defaults for this machine".</summary>
    public string? GamelogPath { get; set; }
    public string? ChatlogPath { get; set; }

    /// <summary>Seconds of history behind the DPS/reps readings.</summary>
    public int StatWindowSeconds { get; set; } = 10;

    /// <summary>How long an EWAR indicator stays lit without a refreshing log line.</summary>
    public int EwarHoldSeconds { get; set; } = 12;

    public bool AlwaysOnTop { get; set; } = true;
    public double Opacity { get; set; } = 0.95;

    /// <summary>Show a floating live preview panel for each running client.</summary>
    public bool ShowPreviews { get; set; } = true;

    /// <summary>Channels shown in the unified chat pane. Empty means all channels.</summary>
    public List<string> ChatChannels { get; set; } = new() { "Fleet", "Local", "Corp", "ViTA Intel" };

    public int ChatScrollbackLines { get; set; } = 2000;

    /// <summary>Per-EWAR-type alert settings, keyed by <see cref="EwarType"/> name.</summary>
    public Dictionary<string, AlertSetting> Alerts { get; set; } = DefaultAlerts();

    /// <summary>Window position of this app's panels, keyed like eve-o-preview by client title.</summary>
    public Dictionary<string, Pt> FlatLayout { get; set; } = new();

    /// <summary>Optional per-active-client layouts: activeClient -> (panel -> position).</summary>
    public Dictionary<string, Dictionary<string, Pt>> PerClientLayout { get; set; } = new();

    public bool EnablePerClientLayouts { get; set; }

    /// <summary>Tracked EVE client window rectangles, mirroring eve-o-preview's ClientLayout map.</summary>
    public Dictionary<string, ClientLayout> ClientLayout { get; set; } = new();

    /// <summary>Character name -> ESI character id, filled by the ESI lookup step.</summary>
    public Dictionary<string, long> CharacterIds { get; set; } = new();

    public Sz PanelSize { get; set; } = new(360, 190);

    private static Dictionary<string, AlertSetting> DefaultAlerts() => new()
    {
        [nameof(EwarType.Jam)] = new AlertSetting { Enabled = true, Tone = "warble", Frequency = 880, DurationMs = 500 },
        [nameof(EwarType.WarpScramble)] = new AlertSetting { Enabled = true, Tone = "descend", Frequency = 440, DurationMs = 450 },
        [nameof(EwarType.WarpDisruption)] = new AlertSetting { Enabled = true, Tone = "double", Frequency = 620, DurationMs = 400 },
        [nameof(EwarType.EnergyNeutralizer)] = new AlertSetting { Enabled = true, Tone = "low", Frequency = 200, DurationMs = 400 },
        // Not emitted by the game log - present so the slot exists if that ever changes.
        [nameof(EwarType.Web)] = new AlertSetting { Enabled = false, Tone = "buzz", Frequency = 320, DurationMs = 400 },
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true
    };

    public static MultiBoxConfig Load(string path)
    {
        if (!File.Exists(path))
            return new MultiBoxConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MultiBoxConfig>(json, Options) ?? new MultiBoxConfig();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public Pt GetPanelLocation(string panelKey, string? activeClient, Pt fallback)
    {
        if (EnablePerClientLayouts && !string.IsNullOrEmpty(activeClient) &&
            PerClientLayout.TryGetValue(activeClient, out var perClient) &&
            perClient.TryGetValue(panelKey, out var scoped))
            return scoped;

        return FlatLayout.TryGetValue(panelKey, out var flat) ? flat : fallback;
    }

    public void SetPanelLocation(string panelKey, string? activeClient, Pt location)
    {
        if (EnablePerClientLayouts)
        {
            if (string.IsNullOrEmpty(activeClient))
                return;
            if (!PerClientLayout.TryGetValue(activeClient, out var perClient))
                PerClientLayout[activeClient] = perClient = new Dictionary<string, Pt>();
            perClient[panelKey] = location;
            return;
        }

        FlatLayout[panelKey] = location;
    }

    /// <summary>eve-o-preview keys everything by window title; this builds the same key.</summary>
    public static string ClientKey(string characterName) => $"EVE - {characterName}";
}

public sealed class AlertSetting
{
    public bool Enabled { get; set; } = true;

    /// <summary>Shape of the generated tone: beep, double, descend, warble, low, buzz.</summary>
    public string Tone { get; set; } = "beep";

    public int Frequency { get; set; } = 600;
    public int DurationMs { get; set; } = 400;

    /// <summary>Optional path to a .wav to play instead of the generated tone.</summary>
    public string? WavPath { get; set; }

    /// <summary>Minimum gap between repeats of this alert, so a cycling scram does not machine-gun.</summary>
    public int CooldownSeconds { get; set; } = 8;
}

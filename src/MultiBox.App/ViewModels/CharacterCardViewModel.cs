using System.Collections.ObjectModel;
using System.Windows.Media;
using MultiBox.Core.Model;
using MultiBox.Core.Stats;

namespace MultiBox.App.ViewModels;

/// <summary>One character card on the fleet grid.</summary>
public sealed class CharacterCardViewModel : ObservableObject
{
    private const int SparkWidth = 100;
    private const int SparkHeight = 22;
    private const int HeavyIncoming = 300;
    /// <summary>
    /// How many log rows to hand the card. The panel flows them into as many columns as fit
    /// and clips the rest, so this is an upper bound rather than what is shown: a tall card
    /// two columns wide displays far more than a short one.
    /// </summary>
    private const int LogRows = 48;

    /// <summary>An em dash reads as "nothing here" without looking like a zero reading.</summary>
    private const string Dash = "—";

    private readonly CharacterMonitor _monitor;

    public CharacterCardViewModel(CharacterMonitor monitor, string role)
    {
        _monitor = monitor;
        Name = monitor.Name;
        CharacterId = monitor.CharacterId;
        _role = role;
    }

    public string Name { get; }
    public long CharacterId { get; }

    private string _role;
    public string Role
    {
        get => _role;
        set { if (Set(ref _role, value)) Raise(nameof(RoleBrush)); }
    }

    public Brush RoleBrush => Palette.ForRole(Role);

    private string _hotkey = string.Empty;
    public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }

    private string? _ship;
    public string? Ship { get => _ship; private set => Set(ref _ship, value); }

    // --- incoming block -----------------------------------------------------------------

    private int _dpsIn;
    public int DpsIn
    {
        get => _dpsIn;
        private set { if (Set(ref _dpsIn, value)) { Raise(nameof(DpsInText)); Raise(nameof(DpsInBrush)); } }
    }

    public string DpsInText => _dpsIn.ToString("N0");

    public Brush DpsInBrush => _dpsIn > HeavyIncoming ? Palette.Danger
        : _dpsIn > 0 ? Palette.DamageLight
        : Palette.TextDisabled;

    private bool _tanking;
    /// <summary>Incoming damage is outrunning the reps landing on this pilot.</summary>
    public bool Tanking { get => _tanking; private set => Set(ref _tanking, value); }

    private bool _stable;
    /// <summary>Taking damage, but the logi is keeping up with it.</summary>
    public bool Stable { get => _stable; private set => Set(ref _stable, value); }

    private PointCollection _sparkIn = new();
    public PointCollection SparkIn { get => _sparkIn; private set => Set(ref _sparkIn, value); }

    private PointCollection _sparkReps = new();
    public PointCollection SparkReps { get => _sparkReps; private set => Set(ref _sparkReps, value); }

    // --- top threat ---------------------------------------------------------------------

    private string? _topThreat;

    /// <summary>Hardest hitter right now: the ship for a player, the NPC name otherwise.</summary>
    public string? TopThreat
    {
        get => _topThreat;
        private set { if (Set(ref _topThreat, value)) Raise(nameof(HasThreat)); }
    }

    private string _topThreatDps = Dash;
    public string TopThreatDps { get => _topThreatDps; private set => Set(ref _topThreatDps, value); }

    private string _topThreatPilot = string.Empty;

    /// <summary>Pilot behind the hull, shown beside it. Empty for an NPC.</summary>
    public string TopThreatPilot { get => _topThreatPilot; private set => Set(ref _topThreatPilot, value); }

    private string _threatTooltip = string.Empty;

    /// <summary>The next few attackers down, so one number does not hide a swarm.</summary>
    public string ThreatTooltip { get => _threatTooltip; private set => Set(ref _threatTooltip, value); }

    public bool HasThreat => _topThreat is not null;

    private void RefreshThreats(DateTime now)
    {
        var top = _monitor.Threats.Top(now, 3);
        if (top.Count == 0)
        {
            TopThreat = null;
            TopThreatPilot = string.Empty;
            TopThreatDps = Dash;
            ThreatTooltip = "Nothing is hitting this character.";
            return;
        }

        var first = top[0];
        TopThreat = first.Label;
        TopThreatPilot = first.Pilot ?? string.Empty;
        TopThreatDps = ((int)Math.Round(first.Dps)).ToString("N0");

        ThreatTooltip = top.Count == 1
            ? $"{first.Describe()} — {first.Dps:F0} dps"
            : "Hitting hardest right now:\n" +
              string.Join("\n", top.Select((t, i) => $"  {i + 1}. {t.Describe()} — {t.Dps:F0} dps"));
    }

    // --- stats row ----------------------------------------------------------------------

    private int _dpsOut;
    public int DpsOut
    {
        get => _dpsOut;
        private set { if (Set(ref _dpsOut, value)) { Raise(nameof(DpsOutText)); Raise(nameof(DpsOutBrush)); } }
    }

    public string DpsOutText => _dpsOut > 0 ? _dpsOut.ToString("N0") : Dash;
    public Brush DpsOutBrush => _dpsOut > 0 ? Palette.DpsOut : Palette.TextDisabled;

    private int _repsIn;
    public int RepsIn
    {
        get => _repsIn;
        private set { if (Set(ref _repsIn, value)) { Raise(nameof(RepsInText)); Raise(nameof(RepsInBrush)); } }
    }

    public string RepsInText => _repsIn > 0 ? _repsIn.ToString("N0") : Dash;
    public Brush RepsInBrush => _repsIn > 0 ? Palette.Success : Palette.TextDisabled;

    private int _capIn;
    public int CapIn
    {
        get => _capIn;
        private set { if (Set(ref _capIn, value)) { Raise(nameof(CapInText)); Raise(nameof(CapInBrush)); } }
    }

    public string CapInText => _capIn > 0 ? "+" + _capIn.ToString("N0") : Dash;
    public Brush CapInBrush => _capIn > 0 ? Palette.Cap : Palette.TextDisabled;

    private int _neutIn;
    public int NeutIn
    {
        get => _neutIn;
        private set { if (Set(ref _neutIn, value)) { Raise(nameof(NeutInText)); Raise(nameof(NeutInBrush)); } }
    }

    public string NeutInText => _neutIn > 0 ? "-" + _neutIn.ToString("N0") : Dash;
    public Brush NeutInBrush => _neutIn > 0 ? Palette.NeutBlue : Palette.TextDisabled;

    // --- EWAR and log -------------------------------------------------------------------

    /// <summary>"G1·3" style tags: which cycle groups this pilot is in, and where in each.</summary>
    public ObservableCollection<string> CycleTags { get; } = new();

    public ObservableCollection<EwarBadgeViewModel> Ewar { get; } = new();
    public ObservableCollection<CombatLogRowViewModel> Log { get; } = new();

    public bool UnderEwar => Ewar.Count > 0;
    public bool NoEwar => Ewar.Count == 0;

    public Brush CardBorderBrush => UnderEwar ? Palette.ThreatBorder : Palette.CardBorder;

    // --- client window ------------------------------------------------------------------

    private bool _clientRunning;
    public bool ClientRunning { get => _clientRunning; set => Set(ref _clientRunning, value); }

    private bool _isForeground;
    public bool IsForeground { get => _isForeground; set => Set(ref _isForeground, value); }

    /// <summary>Handle of this character's EVE window, or zero when it is not running.</summary>
    public IntPtr ClientHandle { get; set; } = IntPtr.Zero;

    private bool _isDragging;
    public bool IsDragging
    {
        get => _isDragging;
        set { if (Set(ref _isDragging, value)) Raise(nameof(CardOpacity)); }
    }

    public double CardOpacity => _isDragging ? 0.4 : 1.0;

    /// <summary>Pulls current values from the monitor. Called on the UI timer.</summary>
    public void Refresh(DateTime wallClockNow)
    {
        // Every window is read on the log's clock rather than ours - see ProjectedNow.
        var now = _monitor.ProjectedNow(wallClockNow);

        DpsIn = (int)Math.Round(_monitor.DamageIn.PerSecond(now));
        DpsOut = (int)Math.Round(_monitor.DamageOut.PerSecond(now));
        RepsIn = (int)Math.Round(_monitor.RepsIn.PerSecond(now));
        CapIn = (int)Math.Round(_monitor.CapTransferIn.PerSecond(now));
        NeutIn = (int)Math.Round(_monitor.NeutIn.PerSecond(now));
        Ship = _monitor.Ship;

        Tanking = DpsIn > RepsIn && DpsIn > 0;
        Stable = DpsIn > 0 && RepsIn >= DpsIn;

        RefreshSparklines();
        RefreshThreats(now);
        RefreshEwar(now);
        RefreshLog();
    }

    private void RefreshSparklines()
    {
        var incoming = _monitor.DamageInHistory.Snapshot();
        var reps = _monitor.RepsInHistory.Snapshot();

        // Both lines share one vertical scale. Drawn independently, a 20 dps trickle would
        // tower like a 2000 dps volley and the pair could not be compared at a glance.
        var ceiling = Math.Max(_monitor.DamageInHistory.Max(), _monitor.RepsInHistory.Max());
        SparkIn = ToPoints(incoming, ceiling);
        SparkReps = ToPoints(reps, ceiling);
    }

    private static PointCollection ToPoints(double[] values, double ceiling)
    {
        var points = new PointCollection(values.Length);
        var step = (double)SparkWidth / Math.Max(1, values.Length - 1);
        var scale = ceiling <= 0 ? 0 : (SparkHeight - 2) / ceiling;

        for (var i = 0; i < values.Length; i++)
            points.Add(new System.Windows.Point(i * step, SparkHeight - 1 - values[i] * scale));

        points.Freeze();
        return points;
    }

    private void RefreshEwar(DateTime now)
    {
        _monitor.Ewar.Expire(now);
        var active = _monitor.Ewar.Active;

        // Rebuild only on a real change: replacing the collection every tick would restart
        // the web badge's flash animation continuously and it would never appear to pulse.
        var changed = active.Count != Ewar.Count ||
                      Ewar.Any(b => !active.ContainsKey(b.Type)) ||
                      Ewar.Any(b => active[b.Type].Describe() != b.Source);

        if (!changed)
            return;

        Ewar.Clear();
        foreach (var pair in active.OrderBy(kv => Palette.ShortName(kv.Key), StringComparer.Ordinal))
            Ewar.Add(new EwarBadgeViewModel(pair.Key, pair.Value.Describe()));

        Raise(nameof(UnderEwar));
        Raise(nameof(NoEwar));
        Raise(nameof(CardBorderBrush));
    }

    private void RefreshLog()
    {
        var recent = _monitor.CombatLog.Recent(LogRows);

        // Cheap identity check on the newest row: rebuilding the list every tick would fight
        // the user for the scroll position.
        if (recent.Count == Log.Count &&
            (recent.Count == 0 || (recent[0].At == Log[0].At && recent[0].Text == Log[0].Text)))
            return;

        Log.Clear();
        foreach (var entry in recent)
            Log.Add(new CombatLogRowViewModel(entry));
    }
}

/// <summary>One EWAR chip: effect type plus who applied it.</summary>
public sealed class EwarBadgeViewModel
{
    public EwarBadgeViewModel(EwarType type, string? source)
    {
        Type = type;
        Label = Palette.ShortName(type);
        Source = source;
        Foreground = Palette.ForEwar(type);
        Background = Palette.Wash(type == EwarType.Web ? Palette.EwarWebBase : Palette.ForEwar(type));

        // A web is the effect you can least afford to miss, and the only one drawn moving
        // rather than merely coloured.
        IsWeb = type == EwarType.Web;
    }

    public EwarType Type { get; }
    public string Label { get; }
    public string? Source { get; }
    public Brush Foreground { get; }
    public Brush Background { get; }
    public bool IsWeb { get; }
}

/// <summary>One row of a character's combat log.</summary>
public sealed class CombatLogRowViewModel
{
    public CombatLogRowViewModel(CombatLogEntry entry)
    {
        At = entry.At;
        Time = entry.At.ToLocalTime().ToString("HH:mm:ss");
        Text = entry.Text;
        Foreground = Palette.ForLogKind(entry.Kind);
    }

    public DateTime At { get; }
    public string Time { get; }
    public string Text { get; }
    public Brush Foreground { get; }
}

using MultiBox.Core.Model;
using MultiBox.Core.Stats;

namespace MultiBox.App.ViewModels;

/// <summary>One pilot's tile: throughput numbers plus the EWAR lamps.</summary>
public sealed class CharacterViewModel : ObservableObject
{
    private readonly CharacterMonitor _monitor;

    public CharacterViewModel(CharacterMonitor monitor)
    {
        _monitor = monitor;
        Name = monitor.Name;
        CharacterId = monitor.CharacterId;

        Indicators = new[]
        {
            new EwarIndicatorViewModel(EwarType.WarpScramble),
            new EwarIndicatorViewModel(EwarType.WarpDisruption),
            new EwarIndicatorViewModel(EwarType.Jam),
            new EwarIndicatorViewModel(EwarType.EnergyNeutralizer),
            new EwarIndicatorViewModel(EwarType.Web)
        };
    }

    public string Name { get; }
    public long CharacterId { get; }
    public IReadOnlyList<EwarIndicatorViewModel> Indicators { get; }

    private string? _ship;
    public string? Ship { get => _ship; private set => Set(ref _ship, value); }

    private double _dpsOut;
    public double DpsOut { get => _dpsOut; private set => Set(ref _dpsOut, value); }

    private double _dpsIn;
    public double DpsIn { get => _dpsIn; private set => Set(ref _dpsIn, value); }

    private double _repsOut;
    public double RepsOut { get => _repsOut; private set => Set(ref _repsOut, value); }

    private double _repsIn;
    public double RepsIn { get => _repsIn; private set => Set(ref _repsIn, value); }

    private double _neutIn;
    public double NeutIn { get => _neutIn; private set => Set(ref _neutIn, value); }

    private bool _clientRunning;
    public bool ClientRunning { get => _clientRunning; set => Set(ref _clientRunning, value); }

    private bool _isForeground;
    public bool IsForeground { get => _isForeground; set => Set(ref _isForeground, value); }

    /// <summary>True while any EWAR effect is on this pilot - drives the tile's alarm border.</summary>
    public bool UnderEwar => Indicators.Any(i => i.IsActive);

    public string PortraitUrl => $"https://images.evetech.net/characters/{CharacterId}/portrait?size=64";

    /// <summary>Pulls current values from the monitor. Called on the UI timer.</summary>
    public void Refresh(DateTime now)
    {
        DpsOut = _monitor.DamageOut.PerSecond(now);
        DpsIn = _monitor.DamageIn.PerSecond(now);
        RepsOut = _monitor.RepsOut.PerSecond(now);
        RepsIn = _monitor.RepsIn.PerSecond(now);
        NeutIn = _monitor.NeutIn.PerSecond(now);
        Ship = _monitor.Ship;

        foreach (var indicator in Indicators)
        {
            var active = _monitor.Ewar.Active.TryGetValue(indicator.Type, out var state);
            indicator.Update(active, active ? state!.Source : null, now);
        }

        Raise(nameof(UnderEwar));
    }
}

public sealed class EwarIndicatorViewModel : ObservableObject
{
    public EwarIndicatorViewModel(EwarType type)
    {
        Type = type;
        Label = EwarTypeInfo.DisplayName(type);
        IsObservable = EwarTypeInfo.IsLogged(type);
    }

    public EwarType Type { get; }
    public string Label { get; }

    /// <summary>
    /// False for effects the client never writes to the log (webs, painters, damps).
    /// The lamp is drawn greyed with a tooltip rather than silently sitting dark, so an
    /// effect that cannot be detected is never mistaken for one that is not happening.
    /// </summary>
    public bool IsObservable { get; }

    private bool _isActive;
    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }

    private string? _source;
    public string? Source { get => _source; private set => Set(ref _source, value); }

    public string Tooltip => IsObservable
        ? IsActive ? $"{Label} by {Source ?? "unknown"}" : $"No {Label.ToLowerInvariant()} detected"
        : $"{Label}: EVE does not write this to the game log, so it cannot be detected";

    internal void Update(bool active, string? source, DateTime now)
    {
        IsActive = active;
        Source = source;
        Raise(nameof(Tooltip));
    }
}

using MultiBox.Core.Model;

namespace MultiBox.Core.Stats;

/// <summary>
/// Tracks which EWAR effects are currently on a character.
///
/// The game log records an effect being *applied*, never released, so "am I still
/// scrammed?" is answered by a hold time: each new line refreshes the effect, and it lapses
/// if nothing refreshes it. Scrams and disruptors re-log on every activation cycle, so a
/// hold slightly longer than a cycle tracks reality closely.
/// </summary>
public sealed class EwarStateTracker
{
    private readonly Dictionary<EwarType, ActiveEwar> _active = new();
    private readonly TimeSpan _hold;

    public EwarStateTracker(TimeSpan? hold = null) => _hold = hold ?? TimeSpan.FromSeconds(12);

    /// <summary>Raised the first time an effect goes from clear to applied - the alert trigger.</summary>
    public event Action<ActiveEwar>? EwarApplied;

    /// <summary>Raised when an effect lapses because nothing refreshed it.</summary>
    public event Action<EwarType>? EwarCleared;

    public IReadOnlyDictionary<EwarType, ActiveEwar> Active => _active;

    public bool IsActive(EwarType type, DateTime now)
    {
        Expire(now);
        return _active.ContainsKey(type);
    }

    /// <summary>Feeds an incoming EWAR event. Returns true when this starts a new application.</summary>
    public bool Apply(EwarType type, DateTime at, string? source, string? module, string? sourceShip = null)
    {
        Expire(at);

        if (_active.TryGetValue(type, out var existing))
        {
            existing.Refresh(at, source, module, sourceShip);
            return false;
        }

        var active = new ActiveEwar(type, at, source, module, sourceShip);
        _active[type] = active;
        EwarApplied?.Invoke(active);
        return true;
    }

    /// <summary>Drops effects whose hold time has elapsed. Call on a timer so the UI clears.</summary>
    public void Expire(DateTime now)
    {
        if (_active.Count == 0)
            return;

        List<EwarType>? lapsed = null;
        foreach (var (type, state) in _active)
        {
            if (now - state.LastSeen > _hold)
                (lapsed ??= new()).Add(type);
        }

        if (lapsed is null)
            return;

        foreach (var type in lapsed)
        {
            _active.Remove(type);
            EwarCleared?.Invoke(type);
        }
    }
}

public sealed class ActiveEwar
{
    internal ActiveEwar(EwarType type, DateTime at, string? source, string? module, string? sourceShip = null)
    {
        Type = type;
        FirstSeen = at;
        LastSeen = at;
        Source = source;
        Module = module;
        SourceShip = sourceShip;
        Count = 1;
    }

    public EwarType Type { get; }
    public DateTime FirstSeen { get; }
    public DateTime LastSeen { get; private set; }
    public string? Source { get; private set; }
    public string? Module { get; private set; }

    /// <summary>Ship the aggressor was flying, when the line named it.</summary>
    public string? SourceShip { get; private set; }

    public int Count { get; private set; }

    /// <summary>Badge text: ship then pilot, the way the log identifies an aggressor.</summary>
    public string? Describe() => (SourceShip, Source) switch
    {
        (not null, not null) => SourceShip + " \"" + Source + "\"",
        (not null, null) => SourceShip,
        (null, not null) => Source,
        _ => null
    };

    internal void Refresh(DateTime at, string? source, string? module, string? sourceShip = null)
    {
        if (at > LastSeen)
            LastSeen = at;
        Count++;
        if (source is not null)
            Source = source;
        if (module is not null)
            Module = module;
        if (sourceShip is not null)
            SourceShip = sourceShip;
    }
}

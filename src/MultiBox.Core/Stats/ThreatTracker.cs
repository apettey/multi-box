using MultiBox.Core.Model;

namespace MultiBox.Core.Stats;

/// <summary>One attacker's current contribution to a character's incoming damage.</summary>
/// <param name="Key">Stable identity — the pilot's name, or the NPC's.</param>
/// <param name="Label">What to show: the ship for a player, the NPC's own name otherwise.</param>
/// <param name="Pilot">Pilot behind the ship, or null for an NPC.</param>
/// <param name="Dps">Damage per second over the tracker's window.</param>
/// <param name="TotalInWindow">Raw damage in the window, for ranking ties and tooltips.</param>
public readonly record struct Threat(string Key, string Label, string? Pilot, double Dps, long TotalInWindow)
{
    /// <summary>Ship plus pilot for a player, bare name for an NPC.</summary>
    public string Describe() => Pilot is null ? Label : $"{Label} \"{Pilot}\"";
}

/// <summary>
/// Splits a character's incoming damage by who is dealing it, so the card can answer the
/// question the raw total cannot: *what* is killing me.
///
/// A single number tells you that 600 dps is landing. It does not tell you whether that is
/// one battleship you could burn away from or twelve frigates you cannot, and those call for
/// opposite decisions.
///
/// Attribution is per attacker over the same rolling window as the headline figure, so the
/// parts always sum to something close to the whole rather than to a fight-long total.
/// </summary>
public sealed class ThreatTracker
{
    private sealed class Entry
    {
        public required string Label { get; init; }
        public required string? Pilot { get; init; }
        public required RollingWindow Damage { get; init; }
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _window;

    public ThreatTracker(TimeSpan? window = null) => _window = window ?? TimeSpan.FromSeconds(10);

    /// <summary>How many distinct attackers are currently being tracked.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Credits damage to whoever dealt it. Damage whose source the log did not name is
    /// counted in the headline figure but cannot be attributed, so it is dropped here rather
    /// than lumped under a fake "unknown" attacker that would then top the list.
    /// </summary>
    public void Add(DateTime at, EveEntity? attacker, int amount)
    {
        if (amount <= 0 || attacker is null)
            return;

        var key = attacker.Name;
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (!_entries.TryGetValue(key, out var entry))
        {
            _entries[key] = entry = new Entry
            {
                // A player is identified by the hull you can see on grid; an NPC by its name,
                // which is all the log gives.
                Label = string.IsNullOrWhiteSpace(attacker.Ship) ? attacker.Name : attacker.Ship,
                Pilot = string.IsNullOrWhiteSpace(attacker.Ship) ? null : attacker.Name,
                Damage = new RollingWindow(_window)
            };
        }

        entry.Damage.Add(at, amount);
    }

    /// <summary>Hardest hitters first. Empty when nothing is landing.</summary>
    public IReadOnlyList<Threat> Top(DateTime now, int count = 3)
    {
        Prune(now);

        return _entries
            .Select(kv => new Threat(
                kv.Key,
                kv.Value.Label,
                kv.Value.Pilot,
                kv.Value.Damage.PerSecond(now),
                kv.Value.Damage.TotalInWindow(now)))
            .Where(t => t.TotalInWindow > 0)
            .OrderByDescending(t => t.TotalInWindow)
            .ThenBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, count))
            .ToList();
    }

    /// <summary>The single hardest hitter, or null when nothing is landing.</summary>
    public Threat? Highest(DateTime now) => Top(now, 1).FirstOrDefault() is { Key: not null } t ? t : null;

    /// <summary>
    /// Forgets attackers with nothing left in the window. Without this, a long fight keeps an
    /// entry for every NPC that ever landed a shot, and the dictionary only grows.
    /// </summary>
    private void Prune(DateTime now)
    {
        if (_entries.Count == 0)
            return;

        List<string>? stale = null;
        foreach (var (key, entry) in _entries)
        {
            if (entry.Damage.TotalInWindow(now) == 0)
                (stale ??= new List<string>()).Add(key);
        }

        if (stale is null)
            return;

        foreach (var key in stale)
            _entries.Remove(key);
    }
}

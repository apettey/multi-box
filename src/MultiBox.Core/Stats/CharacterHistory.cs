using MultiBox.Core.Model;

namespace MultiBox.Core.Stats;

/// <summary>
/// A fixed-length trailing history of one metric, driven by a timer rather than by events.
///
/// Sampling on a clock rather than per log line is what makes the sparkline readable: a
/// quiet second has to occupy the same horizontal space as a busy one, otherwise the line
/// compresses and expands with activity and the shape stops meaning anything.
/// </summary>
public sealed class MetricHistory
{
    private readonly double[] _samples;
    private int _next;

    public MetricHistory(int length = 16)
    {
        if (length < 2)
            throw new ArgumentOutOfRangeException(nameof(length), "A sparkline needs at least two points.");
        _samples = new double[length];
    }

    public int Length => _samples.Length;

    public void Add(double value) 
    {
        _samples[_next] = value;
        _next = (_next + 1) % _samples.Length;
    }

    /// <summary>Oldest sample first, so index order matches left-to-right on screen.</summary>
    public double[] Snapshot()
    {
        var result = new double[_samples.Length];
        for (var i = 0; i < _samples.Length; i++)
            result[i] = _samples[(_next + i) % _samples.Length];
        return result;
    }

    public double Max()
    {
        var max = 0d;
        foreach (var v in _samples)
            if (v > max) max = v;
        return max;
    }
}

/// <summary>One rendered line in a character's combat log.</summary>
public sealed record CombatLogEntry(DateTime At, string Text, CombatLogKind Kind);

/// <summary>
/// Newest-first ring of recent combat lines for one character. Bounded because a long fight
/// would otherwise grow without limit behind a panel that only ever shows a handful of rows.
/// </summary>
public sealed class CombatLogBuffer
{
    private readonly LinkedList<CombatLogEntry> _entries = new();
    private readonly int _capacity;

    public CombatLogBuffer(int capacity = 80) => _capacity = capacity;

    public int Count => _entries.Count;

    public void Add(CombatLogEntry entry)
    {
        _entries.AddFirst(entry);
        while (_entries.Count > _capacity)
            _entries.RemoveLast();
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<CombatLogEntry> Recent(int count) =>
        _entries.Take(count).ToList();
}

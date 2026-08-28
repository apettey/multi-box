namespace MultiBox.Core.Stats;

/// <summary>
/// Sums timestamped amounts over a trailing window and divides by its length, giving the
/// same "last N seconds" reading PyEveLiveDPS shows. Time is passed in rather than read
/// from the clock so replaying a log file produces identical numbers to a live session.
/// </summary>
public sealed class RollingWindow
{
    private readonly Queue<(DateTime At, int Amount)> _samples = new();
    private readonly TimeSpan _window;
    private long _total;

    public RollingWindow(TimeSpan window) => _window = window;

    public TimeSpan Window => _window;

    public void Add(DateTime at, int amount)
    {
        if (amount <= 0)
            return;
        _samples.Enqueue((at, amount));
        _total += amount;
        Evict(at);
    }

    /// <summary>Amount per second over the window, after discarding samples that fell out of it.</summary>
    public double PerSecond(DateTime now)
    {
        Evict(now);
        return _window.TotalSeconds <= 0 ? 0 : _total / _window.TotalSeconds;
    }

    public long TotalInWindow(DateTime now)
    {
        Evict(now);
        return _total;
    }

    private void Evict(DateTime now)
    {
        var cutoff = now - _window;
        while (_samples.Count > 0 && _samples.Peek().At < cutoff)
            _total -= _samples.Dequeue().Amount;
    }
}

using MultiBox.Core.Model;

namespace MultiBox.Core.Chat;

/// <summary>
/// Collapses the N copies of every message - one per running client that is in the channel -
/// into a single row, and records which characters saw it.
/// </summary>
public sealed class ChatAggregator
{
    private readonly Dictionary<string, UnifiedMessage> _byKey = new();
    private readonly LinkedList<UnifiedMessage> _order = new();
    private readonly int _capacity;

    public ChatAggregator(int capacity = 2000) => _capacity = capacity;

    public IReadOnlyCollection<UnifiedMessage> Messages => _order;

    /// <summary>
    /// Adds a message. Returns the unified row when this is the first sighting (the caller
    /// should display it), or null when it is a duplicate from another client.
    /// </summary>
    public UnifiedMessage? Add(ChatMessage message)
    {
        var key = message.DedupKey();
        if (_byKey.TryGetValue(key, out var existing))
        {
            existing.AddWitness(message.Listener);
            return null;
        }

        var unified = new UnifiedMessage(message);
        _byKey[key] = unified;
        _order.AddLast(unified);
        Trim();
        return unified;
    }

    /// <summary>
    /// Drops messages older than <paramref name="maxAge"/>. Returns how many went.
    ///
    /// Local from four hours ago is not comms, it is ballast: it pushes the line you actually
    /// want off the panel and keeps the dedup table holding rows nobody will ever look at.
    /// The list is oldest-first, so this only ever walks the expired prefix.
    /// </summary>
    public int PruneOlderThan(DateTime now, TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero)
            return 0;

        var cutoff = now - maxAge;
        var removed = 0;

        while (_order.First is { Value: var oldest } && oldest.Timestamp < cutoff)
        {
            _order.RemoveFirst();
            _byKey.Remove(oldest.Key);
            removed++;
        }

        return removed;
    }

    private void Trim()
    {
        while (_order.Count > _capacity)
        {
            var oldest = _order.First!.Value;
            _order.RemoveFirst();
            _byKey.Remove(oldest.Key);
        }
    }
}

public sealed class UnifiedMessage
{
    private readonly HashSet<string> _witnesses = new(StringComparer.OrdinalIgnoreCase);

    internal UnifiedMessage(ChatMessage source)
    {
        Key = source.DedupKey();
        Timestamp = source.Timestamp;
        Channel = source.Channel;
        Sender = source.Sender;
        Text = source.Text;
        IsSystem = source.IsSystem;
        _witnesses.Add(source.Listener);
    }

    public string Key { get; }
    public DateTime Timestamp { get; }
    public string Channel { get; }
    public string Sender { get; }
    public string Text { get; }
    public bool IsSystem { get; }

    /// <summary>Which of your characters had this message in their client.</summary>
    public IReadOnlyCollection<string> Witnesses => _witnesses;

    internal void AddWitness(string listener) => _witnesses.Add(listener);

    public override string ToString() => $"[{Timestamp:HH:mm:ss}] {Channel} | {Sender} > {Text}";
}

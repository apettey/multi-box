using System.Collections.Concurrent;
using MultiBox.Core.Chat;
using MultiBox.Core.Config;
using MultiBox.Core.Model;
using MultiBox.Core.Parsing;
using MultiBox.Core.Stats;
using MultiBox.Core.Tailing;

namespace MultiBox.Core;

/// <summary>
/// Owns the whole pipeline: discovers log files, tails them, parses lines, updates
/// per-character stats and feeds the unified chat view. The UI subscribes to its events
/// and never touches a file itself.
/// </summary>
public sealed class MultiBoxSession : IDisposable
{
    private readonly MultiBoxConfig _config;
    private readonly ConcurrentDictionary<string, TailedFile> _files = new();
    private readonly Dictionary<long, CharacterMonitor> _monitors = new();
    private readonly Dictionary<long, string> _namesById = new();
    private readonly HashSet<string> _seenEventKeys = new();
    private readonly Queue<string> _seenEventOrder = new();
    private readonly object _gate = new();

    public MultiBoxSession(MultiBoxConfig config)
    {
        _config = config;
        Chat = new ChatAggregator(config.ChatScrollbackLines);
    }

    public ChatAggregator Chat { get; }

    public IReadOnlyCollection<CharacterMonitor> Characters
    {
        get { lock (_gate) return _monitors.Values.ToList(); }
    }

    /// <summary>Fired once per distinct game event, after cross-client duplicates are dropped.</summary>
    public event Action<GameLogEvent>? CombatEvent;

    /// <summary>Fired when an EWAR effect newly lands on one of your characters.</summary>
    public event Action<string, ActiveEwar>? EwarAlert;

    /// <summary>Fired once per distinct chat message.</summary>
    public event Action<UnifiedMessage>? ChatMessageAdded;

    /// <summary>Scans the log folders and starts tailing anything new.</summary>
    public void Refresh(DateTime? sessionsSince = null)
    {
        var gamelogs = LogDirectory.FindGamelogFolder(_config.GamelogPath);
        if (gamelogs is not null)
        {
            foreach (var file in LogDirectory.LatestPerCharacter(gamelogs, sessionsSince))
                TryOpen(file, isChat: false);
        }

        var chatlogs = LogDirectory.FindChatlogFolder(_config.ChatlogPath);
        if (chatlogs is not null)
        {
            foreach (var file in LogDirectory.LatestPerCharacter(chatlogs, sessionsSince))
            {
                if (_config.ChatChannels.Count > 0 && file.Channel is not null &&
                    !_config.ChatChannels.Contains(file.Channel, StringComparer.OrdinalIgnoreCase))
                    continue;
                TryOpen(file, isChat: true);
            }
        }
    }

    /// <summary>Reads whatever has been appended since the last poll. Call on a timer.</summary>
    public void Poll()
    {
        foreach (var file in _files.Values.ToList())
        {
            IReadOnlyList<string> lines;
            try
            {
                lines = file.Tailer.ReadNewLines();
            }
            catch (IOException)
            {
                // OneDrive can momentarily lock a file mid-sync; the next poll retries.
                continue;
            }

            foreach (var line in lines)
                Handle(file, line);
        }

        var now = DateTime.UtcNow;
        lock (_gate)
        {
            foreach (var monitor in _monitors.Values)
                monitor.Ewar.Expire(now);
        }
    }

    private void Handle(TailedFile file, string line)
    {
        if (file.ChatParser is not null)
        {
            var message = file.ChatParser.ParseLine(line);
            if (message is null)
                return;
            var unified = Chat.Add(message);
            if (unified is not null)
                ChatMessageAdded?.Invoke(unified);
            return;
        }

        var evt = file.GameParser?.ParseLine(line);
        if (evt is null)
            return;

        // The same event is written to every client's log that could see it. Collapse to one.
        if (!MarkSeen(evt.DedupKey()))
            return;

        lock (_gate)
        {
            foreach (var monitor in _monitors.Values)
                monitor.Apply(evt);
        }

        CombatEvent?.Invoke(evt);
    }

    private bool MarkSeen(string key)
    {
        lock (_gate)
        {
            if (!_seenEventKeys.Add(key))
                return false;

            _seenEventOrder.Enqueue(key);
            while (_seenEventOrder.Count > 20000)
                _seenEventKeys.Remove(_seenEventOrder.Dequeue());

            return true;
        }
    }

    private void TryOpen(LogFileName file, bool isChat)
    {
        if (_files.ContainsKey(file.Path))
            return;

        LogHeader header;
        try
        {
            header = LogHeader.Parse(ReadHeaderLines(file.Path));
        }
        catch (IOException)
        {
            return;
        }

        var listener = header.Listener;
        if (string.IsNullOrWhiteSpace(listener))
            return;

        lock (_gate)
        {
            _namesById[file.CharacterId] = listener;
            if (!isChat && !_monitors.ContainsKey(file.CharacterId))
            {
                var monitor = new CharacterMonitor(listener, file.CharacterId,
                    TimeSpan.FromSeconds(_config.StatWindowSeconds),
                    TimeSpan.FromSeconds(_config.EwarHoldSeconds));
                var name = listener;
                monitor.Ewar.EwarApplied += active => EwarAlert?.Invoke(name, active);
                _monitors[file.CharacterId] = monitor;
            }
        }

        LogTailer tailer;
        try
        {
            // Start at the end: history has already happened, and replaying it would fire
            // alerts for tackle that landed before the app was even running.
            tailer = new LogTailer(file.Path, fromStart: false);
        }
        catch (IOException)
        {
            return;
        }

        _files[file.Path] = new TailedFile
        {
            Tailer = tailer,
            GameParser = isChat ? null : new GamelogParser(listener),
            ChatParser = isChat ? new ChatlogParser(header.ChannelName ?? file.Channel ?? "Unknown", listener) : null
        };
    }

    private static IEnumerable<string> ReadHeaderLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        for (var i = 0; i < 12; i++)
        {
            var line = reader.ReadLine();
            if (line is null)
                yield break;
            yield return ChatlogParser.Clean(line);
        }
    }

    /// <summary>Character name to id, learned from log filenames and banners.</summary>
    public IReadOnlyDictionary<string, long> KnownCharacters()
    {
        lock (_gate)
            return _namesById.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        foreach (var file in _files.Values)
            file.Tailer.Dispose();
        _files.Clear();
    }

    private sealed class TailedFile
    {
        public required LogTailer Tailer { get; init; }
        public GamelogParser? GameParser { get; init; }
        public ChatlogParser? ChatParser { get; init; }
    }
}

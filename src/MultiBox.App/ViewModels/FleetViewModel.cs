using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using MultiBox.App.Alerts;
using MultiBox.App.Interop;
using MultiBox.Core;
using MultiBox.Core.Chat;
using MultiBox.Core.Config;
using MultiBox.Core.Model;
using MultiBox.Core.Stats;

namespace MultiBox.App.ViewModels;

/// <summary>
/// The whole dashboard: every character card, the fleet totals, and the unified comms feed.
/// </summary>
public sealed class FleetViewModel : ObservableObject, IDisposable
{
    /// <summary>Filter chips that always lead the row, in this order, before the rest sort.</summary>
    private static readonly string[] PinnedChannels = { "ALL", "CORP", "ALLIANCE", "LOCAL" };

    private readonly MultiBoxSession _session;
    private readonly MultiBoxConfig _config;
    private readonly AlertPlayer _alerts;
    private readonly VoiceAnnouncer _voice;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _historyTimer;
    private readonly DispatcherTimer _rescanTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly Dictionary<long, CharacterCardViewModel> _cards = new();
    private readonly List<CharacterCardViewModel> _all = new();

    public FleetViewModel(MultiBoxConfig config, string configPath)
    {
        _config = config;
        ConfigPath = configPath;
        _session = new MultiBoxSession(config);
        _alerts = new AlertPlayer(config);
        _voice = new VoiceAnnouncer { Enabled = config.VoiceAlerts };

        _session.EwarAlert += OnEwarAlert;
        _session.ChatMessageAdded += message => Dispatch(() => AddMessage(message));

        // Reading appended log text is cheap, so a short interval keeps alerts responsive.
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pollTimer.Tick += (_, _) => Tick();

        // Sparklines advance on their own clock: sampling per event would make the shape a
        // function of how busy the fight is rather than of time.
        _historyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _historyTimer.Tick += (_, _) => SampleHistory();

        // Rescanning the folder is comparatively expensive and only needs to notice new
        // session files: a dock, a jump clone, a client starting.
        _rescanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _rescanTimer.Tick += (_, _) => Rescan();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            EveClock = DateTime.UtcNow.ToString("HH:mm:ss") + " EVE";
            PruneOldMessages();
        };

        BuildChannelChips();
    }

    public string ConfigPath { get; }

    /// <summary>Cards on the active squad tab.</summary>
    public ObservableCollection<CharacterCardViewModel> Cards { get; } = new();

    public ObservableCollection<CommsRowViewModel> Messages { get; } = new();
    public ObservableCollection<ChannelChipViewModel> Channels { get; } = new();
    public ObservableCollection<SquadTabViewModel> Squads { get; } = new();

    // --- header readouts -----------------------------------------------------------------

    private string _fleetIncoming = "0";
    public string FleetIncoming { get => _fleetIncoming; private set => Set(ref _fleetIncoming, value); }

    private string _fleetReps = "0";
    public string FleetReps { get => _fleetReps; private set => Set(ref _fleetReps, value); }

    private string _fleetNeut = "0";
    public string FleetNeut { get => _fleetNeut; private set => Set(ref _fleetNeut, value); }

    private int _ewarAlerts;
    public int EwarAlerts
    {
        get => _ewarAlerts;
        private set { if (Set(ref _ewarAlerts, value)) Raise(nameof(EwarAlertBrush)); }
    }

    public Brush EwarAlertBrush => _ewarAlerts > 0 ? Palette.Danger : Palette.Success;

    private string _eveClock = string.Empty;
    public string EveClock { get => _eveClock; private set => Set(ref _eveClock, value); }

    private string _status = "Starting…";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private int _mergedCount;
    public int MergedCount
    {
        get => _mergedCount;
        private set { if (Set(ref _mergedCount, value)) Raise(nameof(MergedText)); }
    }

    public string MergedText => $"{_mergedCount:N0} dupes merged";

    // --- grid shape ----------------------------------------------------------------------

    private int _gridColumns = 5;
    public int GridColumns { get => _gridColumns; private set => Set(ref _gridColumns, value); }

    private int _gridRows = 2;
    public int GridRows { get => _gridRows; private set => Set(ref _gridRows, value); }

    private double _cardAreaAspect = FleetLayout.DefaultAreaAspect;

    /// <summary>
    /// Width over height of the area the cards occupy, so the grid can pick a shape that
    /// actually fits it. Two cards belong side by side on a wide monitor and stacked on a
    /// tall one, and only the measurement can tell which.
    /// </summary>
    public void SetCardAreaAspect(double aspect)
    {
        if (double.IsNaN(aspect) || double.IsInfinity(aspect) || aspect <= 0)
            return;

        // Ignore noise: re-laying out every card on a one-pixel resize would thrash.
        if (Math.Abs(aspect - _cardAreaAspect) < 0.02)
            return;

        _cardAreaAspect = aspect;
        ApplyGridShape();
    }

    private void ApplyGridShape()
    {
        var (columns, rows) = FleetLayout.Grid(Cards.Count, _cardAreaAspect);
        GridColumns = columns;
        GridRows = rows;
    }

    /// <summary>Cards per squad tab, adjustable from the header.</summary>
    public int SquadSize
    {
        get => _config.SquadSize;
        set
        {
            var clamped = Math.Clamp(value, 1, 20);
            if (_config.SquadSize == clamped)
                return;
            _config.SquadSize = clamped;
            Raise(nameof(SquadSize));
            RebuildSquads();
            ShowSquad(_activeSquad);
        }
    }

    /// <summary>Hide characters whose client is closed.</summary>
    public bool OnlyRunningClients
    {
        get => _config.ShowOnlyRunningClients;
        set
        {
            if (_config.ShowOnlyRunningClients == value)
                return;
            _config.ShowOnlyRunningClients = value;
            Raise(nameof(OnlyRunningClients));
            RefreshVisible();
        }
    }

    // --- settings ------------------------------------------------------------------------

    public bool ShowThumbnails
    {
        get => _config.ShowPreviews;
        set { _config.ShowPreviews = value; Raise(nameof(ShowThumbnails)); ThumbnailsChanged?.Invoke(); }
    }

    public bool AlertFlash
    {
        get => _config.AlertFlash;
        set { _config.AlertFlash = value; Raise(nameof(AlertFlash)); }
    }

    public bool VoiceAlerts
    {
        get => _config.VoiceAlerts;
        set { _config.VoiceAlerts = value; _voice.Enabled = value; Raise(nameof(VoiceAlerts)); }
    }

    /// <summary>Keep the newest message in view as it arrives.</summary>
    public bool FollowChat
    {
        get => _config.FollowChat;
        set
        {
            if (_config.FollowChat == value)
                return;
            _config.FollowChat = value;
            Raise(nameof(FollowChat));

            // Turning it back on should catch up immediately rather than at the next message.
            if (value)
                ScrollToNewest?.Invoke();
        }
    }

    /// <summary>Raised when the panel should jump back to the newest message.</summary>
    public event Action? ScrollToNewest;

    public bool Muted
    {
        get => _alerts.Muted;
        set { _alerts.Muted = value; Raise(nameof(Muted)); }
    }

    public event Action? ThumbnailsChanged;

    // --- lifecycle -----------------------------------------------------------------------

    public void Start()
    {
        Rescan();

        // The session has already read this run's chat history by now. Without this the
        // panel starts blank and only fills as new lines arrive, which on a quiet evening
        // looks exactly like the chat tailing being broken.
        RebuildMessages();
        BuildChannelChips();

        _pollTimer.Start();
        _historyTimer.Start();
        _rescanTimer.Start();
        _clockTimer.Start();
    }

    private void Tick()
    {
        _session.Poll();
        var now = DateTime.UtcNow;

        foreach (var card in _all)
            card.Refresh(now);

        var incoming = Cards.Sum(c => c.DpsIn);
        var reps = Cards.Sum(c => c.RepsIn);
        var neut = Cards.Sum(c => c.NeutIn);

        FleetIncoming = incoming.ToString("N0");
        FleetReps = reps.ToString("N0");
        FleetNeut = neut > 0 ? "-" + neut.ToString("N0") : "0";
        EwarAlerts = Cards.Sum(c => c.Ewar.Count);
    }

    private void SampleHistory()
    {
        var now = DateTime.UtcNow;
        foreach (var monitor in _session.Characters)
            monitor.SampleHistory(monitor.ProjectedNow(now));
    }

    private void Rescan()
    {
        // Only this session's files: replaying yesterday's combat would be noise.
        _session.Refresh(DateTime.UtcNow.Date.AddDays(-1));

        var added = false;
        foreach (var monitor in _session.Characters)
        {
            if (_cards.ContainsKey(monitor.CharacterId))
                continue;

            var role = _config.CharacterRoles.TryGetValue(monitor.Name, out var configured)
                ? configured
                : "DPS";

            var card = new CharacterCardViewModel(monitor, role);
            _cards[monitor.CharacterId] = card;
            _all.Add(card);
            added = true;
        }

        if (added)
            ApplyOrder();

        TrackClientWindows();
        RefreshCycleTags();
        RefreshVisible();

        Status = _all.Count == 0
            ? "No EVE logs found yet — waiting for a client to write one."
            : $"Watching {_all.Count} character(s).";
    }

    /// <summary>Matches cards to running client windows so thumbnails and focus have a target.</summary>
    private void TrackClientWindows()
    {
        try
        {
            var running = EveClientLocator.FindClients();
            var foreground = running.FirstOrDefault(c => c.IsForeground)?.CharacterName;

            foreach (var card in _all)
            {
                var client = running.FirstOrDefault(c =>
                    c.CharacterName.Equals(card.Name, StringComparison.OrdinalIgnoreCase));

                card.ClientHandle = client?.Handle ?? IntPtr.Zero;
                card.ClientRunning = client is not null;
                card.IsForeground = foreground is not null &&
                    foreground.Equals(card.Name, StringComparison.OrdinalIgnoreCase);
            }

            EveClientLocator.TrackLayouts(_config);
        }
        catch (Exception)
        {
            // Window enumeration is a nicety; the numbers work without it.
        }
    }

    // --- ordering and squads --------------------------------------------------------------

    /// <summary>
    /// Sorts cards by the saved order, then rebuilds the squad tabs. Names not in the saved
    /// order append in discovery order, so a newly logged-in character never displaces one.
    /// </summary>
    private void ApplyOrder()
    {
        var saved = _config.CharacterOrder;
        _all.Sort((a, b) =>
        {
            var ia = saved.FindIndex(n => n.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
            var ib = saved.FindIndex(n => n.Equals(b.Name, StringComparison.OrdinalIgnoreCase));
            if (ia < 0) ia = int.MaxValue;
            if (ib < 0) ib = int.MaxValue;
            return ia != ib ? ia.CompareTo(ib) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        RebuildSquads();
        ShowSquad(Math.Min(_activeSquad, Math.Max(0, Squads.Count - 1)));
    }

    private int _activeSquad;

    /// <summary>
    /// The characters eligible for a card: everyone, or only those with a client open.
    /// Ordering is the saved order, so hiding a closed client does not reshuffle the rest.
    /// </summary>
    private List<CharacterCardViewModel> Visible() =>
        _config.ShowOnlyRunningClients
            ? _all.Where(c => c.ClientRunning).ToList()
            : _all.ToList();

    /// <summary>Re-evaluates who is on the grid. Cheap, and called whenever clients change.</summary>
    private void RefreshVisible()
    {
        var visible = Visible();
        var unchanged = visible.Count == _lastVisibleCount;
        _lastVisibleCount = visible.Count;

        RebuildSquads();

        // Rebuilding the card list resets scroll positions and restarts animations, so only
        // do it when the set actually changed.
        if (!unchanged || Cards.Count == 0)
            ShowSquad(_activeSquad);

        HiddenCount = _all.Count - visible.Count;
    }

    private int _lastVisibleCount = -1;

    private int _hiddenCount;

    /// <summary>Characters known from logs whose client is closed.</summary>
    public int HiddenCount
    {
        get => _hiddenCount;
        private set { if (Set(ref _hiddenCount, value)) Raise(nameof(HiddenText)); }
    }

    public string HiddenText => _hiddenCount > 0 ? $"{_hiddenCount} closed" : string.Empty;

    private void RebuildSquads()
    {
        var visible = Visible();
        var size = Math.Max(1, _config.SquadSize);
        var count = FleetLayout.SquadCount(visible.Count, size);

        Squads.Clear();
        for (var i = 0; i < count; i++)
        {
            var members = FleetLayout.Squad(visible, i, size).Count();
            Squads.Add(new SquadTabViewModel(i, $"SQUAD {i + 1}", members));
        }

        if (_activeSquad >= Squads.Count)
            _activeSquad = Squads.Count - 1;
    }

    public void ShowSquad(int index)
    {
        if (Squads.Count == 0)
            return;

        _activeSquad = Math.Clamp(index, 0, Squads.Count - 1);

        Cards.Clear();
        var members = FleetLayout.Squad(Visible(), _activeSquad, _config.SquadSize).ToList();
        for (var i = 0; i < members.Count; i++)
        {
            // F1-F12 label the first twelve; past that there is no function key to name.
            members[i].Hotkey = i < 12 ? "F" + (i + 1) : string.Empty;
            Cards.Add(members[i]);
        }

        foreach (var tab in Squads)
            tab.IsSelected = tab.Index == _activeSquad;

        ApplyGridShape();
    }

    /// <summary>Moves a card to another card's position and remembers the new order.</summary>
    public void Reorder(CharacterCardViewModel moved, CharacterCardViewModel target)
    {
        if (ReferenceEquals(moved, target))
            return;

        var from = _all.IndexOf(moved);
        var to = _all.IndexOf(target);
        if (from < 0 || to < 0)
            return;

        _all.RemoveAt(from);
        _all.Insert(to, moved);

        _config.CharacterOrder = _all.Select(c => c.Name).ToList();
        RebuildSquads();
        ShowSquad(_activeSquad);
    }

    /// <summary>Every known character in display order, for the ordering window.</summary>
    public IReadOnlyList<CharacterCardViewModel> AllCards => _all;

    /// <summary>Applies an order chosen elsewhere and persists it.</summary>
    public void ApplyOrder(IReadOnlyList<string> names)
    {
        _config.CharacterOrder = names.ToList();
        ApplyOrder();
        RefreshVisible();
    }

    /// <summary>Every character the dashboard knows about, for the cycle-group roster.</summary>
    public IReadOnlyList<string> AllCharacterNames => _all.Select(c => c.Name).ToList();

    /// <summary>Window handle for a character, so the switcher can raise the right client.</summary>
    public IntPtr HandleFor(string character) =>
        _all.FirstOrDefault(c => c.Name.Equals(character, StringComparison.OrdinalIgnoreCase))
            ?.ClientHandle ?? IntPtr.Zero;

    /// <summary>
    /// Recomputes each card's group tags. Shown on the card so the cycle order is legible
    /// from the dashboard rather than only inside the config window.
    /// </summary>
    public void RefreshCycleTags()
    {
        _config.EnsureCycleGroups();

        foreach (var card in _all)
        {
            var tags = new List<string>();
            for (var g = 0; g < _config.CycleGroups.Count; g++)
            {
                var position = _config.CycleGroups[g].Members
                    .FindIndex(m => m.Equals(card.Name, StringComparison.OrdinalIgnoreCase));
                if (position >= 0)
                    tags.Add($"G{g + 1}·{position + 1}");
            }

            if (card.CycleTags.SequenceEqual(tags, StringComparer.Ordinal))
                continue;

            card.CycleTags.Clear();
            foreach (var tag in tags)
                card.CycleTags.Add(tag);
        }
    }

    // --- alerts ----------------------------------------------------------------------------

    private void OnEwarAlert(string character, ActiveEwar active)
    {
        _alerts.Play(character, active.Type);

        // Only the web is spoken. Making every effect talk would mean the one that matters
        // arrives in a queue behind three that do not.
        if (active.Type == EwarType.Web)
            Dispatch(() => _voice.Say($"Web detected. {character}."));

        Dispatch(() => Status =
            $"{character}: {EwarTypeInfo.DisplayName(active.Type)}" +
            (active.Source is null ? "" : $" by {active.Source}"));
    }

    public void TestVoice()
    {
        var who = Cards.FirstOrDefault()?.Name ?? "Commander";
        _voice.Say($"Web detected. {who}.");
    }

    public void PreviewAlert(EwarType type) => _alerts.Preview(type);

    // --- comms ------------------------------------------------------------------------------

    private string _selectedChannel = "ALL";

    /// <summary>
    /// Newest message that was on screen when the panel was last cleared. Rebuilding the list
    /// after a filter change re-reads the session history, which would otherwise bring every
    /// cleared line straight back.
    /// </summary>
    private DateTime _clearedThrough = DateTime.MinValue;

    /// <summary>
    /// Empties the comms view. The session keeps its history and nothing on disk is touched -
    /// this is a "I have read all that" button, not a delete.
    /// </summary>
    public void ClearMessages()
    {
        _clearedThrough = Messages.Count > 0 ? Messages[0].Timestamp : DateTime.UtcNow;
        Messages.Clear();
    }

    private void BuildChannelChips()
    {
        var seen = Messages.Select(m => m.ChannelTag).Distinct(StringComparer.OrdinalIgnoreCase);
        var rest = seen.Where(c => !PinnedChannels.Contains(c, StringComparer.OrdinalIgnoreCase))
                       .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);

        var wanted = PinnedChannels.Concat(rest).ToList();

        if (Channels.Count == wanted.Count &&
            Channels.Select(c => c.Name).SequenceEqual(wanted, StringComparer.OrdinalIgnoreCase))
            return;

        Channels.Clear();
        foreach (var name in wanted)
            Channels.Add(new ChannelChipViewModel(name) { IsSelected = name == _selectedChannel });
    }

    public void SelectChannel(ChannelChipViewModel chip)
    {
        _selectedChannel = chip.Name;
        foreach (var c in Channels)
            c.IsSelected = ReferenceEquals(c, chip);
        RebuildMessages();
    }

    private void AddMessage(UnifiedMessage message)
    {
        MergedCount += Math.Max(0, message.Witnesses.Count - 1);
        BuildChannelChips();

        if (!Matches(message.Channel))
            return;

        // Anything genuinely new is shown, even if a late flush gives it an old timestamp.

        // Newest first, matching how the panel reads.
        Messages.Insert(0, new CommsRowViewModel(message));
        while (Messages.Count > _config.ChatScrollbackLines)
            Messages.RemoveAt(Messages.Count - 1);

        if (_config.FollowChat)
            ScrollToNewest?.Invoke();
    }

    /// <summary>
    /// Drops rows past the retention age, from the panel and from the history behind it.
    ///
    /// Runs on the clock rather than only as messages arrive, so a quiet channel still ages
    /// out instead of leaving an hour-old wall of text sitting there until someone speaks.
    /// </summary>
    private void PruneOldMessages()
    {
        var minutes = _config.ChatRetentionMinutes;
        if (minutes <= 0)
            return;

        var maxAge = TimeSpan.FromMinutes(minutes);
        var cutoff = DateTime.UtcNow - maxAge;

        _session.Chat.PruneOlderThan(DateTime.UtcNow, maxAge);

        // Oldest rows sit at the end, newest first being how the panel reads.
        while (Messages.Count > 0 && Messages[^1].Timestamp < cutoff)
            Messages.RemoveAt(Messages.Count - 1);
    }

    private bool Matches(string channel) =>
        _selectedChannel.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
        channel.Equals(_selectedChannel, StringComparison.OrdinalIgnoreCase);

    private void RebuildMessages()
    {
        Messages.Clear();
        foreach (var message in _session.Chat.Messages
                     .Where(m => Matches(m.Channel) && m.Timestamp > _clearedThrough)
                     .OrderByDescending(m => m.Timestamp)
                     .Take(_config.ChatScrollbackLines))
            Messages.Add(new CommsRowViewModel(message));
    }

    // --- plumbing ---------------------------------------------------------------------------

    public void SaveConfig()
    {
        try { _config.Save(ConfigPath); }
        catch (IOException) { /* config is a convenience, not worth crashing over */ }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _historyTimer.Stop();
        _rescanTimer.Stop();
        _clockTimer.Stop();
        _session.Dispose();
        _alerts.Dispose();
        _voice.Dispose();
    }
}

/// <summary>One squad tab in the header.</summary>
public sealed class SquadTabViewModel : ObservableObject
{
    public SquadTabViewModel(int index, string name, int members)
    {
        Index = index;
        Label = members > 0 ? $"{name} · {members}" : name;
    }

    public int Index { get; }
    public string Label { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value))
                return;
            Raise(nameof(Foreground));
            Raise(nameof(Background));
        }
    }

    public Brush Foreground => _isSelected ? Palette.TextPrimary : Palette.TextMuted;
    public Brush Background => _isSelected ? Palette.CardBorder : Brushes.Transparent;
}

using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using MultiBox.App.Alerts;
using MultiBox.App.Interop;
using MultiBox.Core;
using MultiBox.Core.Chat;
using MultiBox.Core.Config;

namespace MultiBox.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly MultiBoxSession _session;
    private readonly MultiBoxConfig _config;
    private readonly AlertPlayer _alerts;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _rescanTimer;
    private readonly Dictionary<long, CharacterViewModel> _characters = new();

    public MainViewModel(MultiBoxConfig config, string configPath)
    {
        _config = config;
        ConfigPath = configPath;
        _session = new MultiBoxSession(config);
        _alerts = new AlertPlayer(config);

        _session.EwarAlert += (character, active) =>
        {
            _alerts.Play(character, active.Type);
            Dispatch(() => Status = $"{character}: {Core.Model.EwarTypeInfo.DisplayName(active.Type)}" +
                                    (active.Source is null ? "" : $" by {active.Source}"));
        };

        _session.ChatMessageAdded += message => Dispatch(() => AddChat(message));

        // Reading appended log text is cheap; a short interval keeps alerts responsive.
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pollTimer.Tick += (_, _) => Tick();

        // Rescanning the folder is comparatively expensive and only needs to notice new
        // session files (a dock, a jump clone, a client starting).
        _rescanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _rescanTimer.Tick += (_, _) => Rescan();
    }

    public string ConfigPath { get; }

    public ObservableCollection<CharacterViewModel> Characters { get; } = new();
    public ObservableCollection<ChatRowViewModel> Chat { get; } = new();
    public ObservableCollection<string> Channels { get; } = new() { "All" };

    private string _selectedChannel = "All";
    public string SelectedChannel
    {
        get => _selectedChannel;
        set { if (Set(ref _selectedChannel, value)) RebuildChatFilter(); }
    }

    private string _status = "Starting...";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private bool _muted;
    public bool Muted
    {
        get => _muted;
        set { if (Set(ref _muted, value)) _alerts.Muted = value; }
    }

    public void Start()
    {
        Rescan();
        _pollTimer.Start();
        _rescanTimer.Start();
        Status = Characters.Count == 0
            ? "No EVE logs found yet - waiting for a client to write one."
            : $"Watching {Characters.Count} character(s).";
    }

    private void Rescan()
    {
        // Only this session's files: replaying yesterday's combat would be noise.
        _session.Refresh(DateTime.UtcNow.Date.AddDays(-1));

        foreach (var monitor in _session.Characters)
        {
            if (_characters.ContainsKey(monitor.CharacterId))
                continue;
            var vm = new CharacterViewModel(monitor);
            _characters[monitor.CharacterId] = vm;
            Characters.Add(vm);
        }

        try
        {
            var running = EveClientLocator.FindClients();
            var foreground = running.FirstOrDefault(c => c.IsForeground)?.CharacterName;
            foreach (var vm in Characters)
            {
                vm.ClientRunning = running.Any(c =>
                    c.CharacterName.Equals(vm.Name, StringComparison.OrdinalIgnoreCase));
                vm.IsForeground = foreground is not null &&
                    foreground.Equals(vm.Name, StringComparison.OrdinalIgnoreCase);
            }

            if (_config.ClientLayout.Count >= 0)
                EveClientLocator.TrackLayouts(_config);
        }
        catch (Exception)
        {
            // Window enumeration is a nicety; the dashboard works without it.
        }
    }

    private void Tick()
    {
        _session.Poll();
        var now = DateTime.UtcNow;
        foreach (var vm in Characters)
            vm.Refresh(now);
    }

    private void AddChat(UnifiedMessage message)
    {
        if (!Channels.Contains(message.Channel))
            Channels.Add(message.Channel);

        if (SelectedChannel != "All" && !message.Channel.Equals(SelectedChannel, StringComparison.OrdinalIgnoreCase))
            return;

        Chat.Add(new ChatRowViewModel(message));
        while (Chat.Count > _config.ChatScrollbackLines)
            Chat.RemoveAt(0);
    }

    private void RebuildChatFilter()
    {
        Chat.Clear();
        foreach (var message in _session.Chat.Messages)
        {
            if (SelectedChannel == "All" ||
                message.Channel.Equals(SelectedChannel, StringComparison.OrdinalIgnoreCase))
                Chat.Add(new ChatRowViewModel(message));
        }
    }

    public void PreviewAlert(Core.Model.EwarType type) => _alerts.Preview(type);

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
        _rescanTimer.Stop();
        _session.Dispose();
        _alerts.Dispose();
    }
}

/// <summary>A single row in the unified chat pane.</summary>
public sealed class ChatRowViewModel
{
    public ChatRowViewModel(UnifiedMessage message)
    {
        Time = message.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        Channel = message.Channel;
        Sender = message.Sender;
        Text = message.Text;
        IsSystem = message.IsSystem;
        // How many of your clients saw it - "4" means everyone was in the channel.
        Witnesses = message.Witnesses.Count;
        WitnessTooltip = "Seen by: " + string.Join(", ", message.Witnesses);
    }

    public string Time { get; }
    public string Channel { get; }
    public string Sender { get; }
    public string Text { get; }
    public bool IsSystem { get; }
    public int Witnesses { get; }
    public string WitnessTooltip { get; }
}

using System.Collections.ObjectModel;
using System.Windows.Media;
using MultiBox.Core.Config;
using MultiBox.Core.Esi;
using MultiBox.Core.Settings;

namespace MultiBox.App.ViewModels;

/// <summary>
/// The settings sync editor. Choices are written straight into the shared config, like the
/// cycle-group editor, and the sync itself runs only through <see cref="SettingsSyncService"/>,
/// which holds the "no client open" rule.
/// </summary>
public sealed class SettingsSyncViewModel : ObservableObject
{
    private readonly MultiBoxConfig _config;
    private readonly SettingsSyncService _service;
    private readonly Dictionary<long, string> _names = new();

    public SettingsSyncViewModel(MultiBoxConfig config, SettingsSyncService service,
        IReadOnlyDictionary<string, long> knownCharacters)
    {
        _config = config;
        _service = service;

        foreach (var (name, id) in _config.CharacterIds.Concat(knownCharacters))
            if (id > 0)
                _names[id] = name;

        foreach (var profile in EveSettingsLocator.FindProfiles())
            Profiles.Add(profile);

        _selectedProfile = Profiles.FirstOrDefault(p => p.Path.Equals(Settings.ProfilePath, StringComparison.OrdinalIgnoreCase))
                           ?? Profiles.FirstOrDefault();
        if (_selectedProfile is not null)
            Settings.ProfilePath = _selectedProfile.Path;

        LoadRows();
        RefreshClients();
    }

    private SettingsSyncConfig Settings => _config.SettingsSync;

    public ObservableCollection<SettingsProfile> Profiles { get; } = new();
    public ObservableCollection<SettingsSyncRowViewModel> Rows { get; } = new();

    public bool HasProfiles => Profiles.Count > 0;

    private SettingsProfile? _selectedProfile;
    public SettingsProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value) || value is null)
                return;

            // A different profile holds a different set of characters, so the old source
            // and exclusions mean nothing there.
            Settings.ProfilePath = value.Path;
            Settings.SourceCharacterId = 0;
            Settings.ExcludedCharacterIds.Clear();
            LoadRows();
        }
    }

    public bool AutoSync
    {
        get => Settings.AutoSync;
        set { Settings.AutoSync = value; Raise(); }
    }

    private int _openClients;

    private string _clientsText = string.Empty;
    public string ClientsText { get => _clientsText; private set => Set(ref _clientsText, value); }

    private Brush _clientsBrush = Palette.TextMuted;
    public Brush ClientsBrush { get => _clientsBrush; private set => Set(ref _clientsBrush, value); }

    public bool CanSync => _openClients == 0 && Settings.IsConfigured && Rows.Any(r => r.IsTarget);

    public string LastSyncText => Settings.LastSyncUtc is { } last
        ? "Last synced " + last.ToLocalTime().ToString("ddd d MMM HH:mm")
        : "Never synced";

    private string? _message;
    public string? Message { get => _message; set => Set(ref _message, value); }

    /// <summary>Called on a timer: the answer changes as clients open and close.</summary>
    public void RefreshClients()
    {
        _openClients = SettingsSyncService.OpenClients();
        if (_openClients == 0)
        {
            ClientsText = "No EVE clients open — safe to sync.";
            ClientsBrush = Palette.Success;
        }
        else
        {
            ClientsText = _openClients == int.MaxValue
                ? "Could not check for running clients — syncing is disabled."
                : $"{_openClients} EVE client(s) open — close them all to sync. A running client writes its own settings back when it logs out.";
            ClientsBrush = Palette.Danger;
        }

        Raise(nameof(CanSync));
    }

    public void LoadRows()
    {
        Rows.Clear();
        if (_selectedProfile is null)
            return;

        var files = EveSettingsLocator.CharacterFiles(_selectedProfile.Path);

        // Default the source to the most recently used character: the one whose layout the
        // player was most likely just arranging.
        if (Settings.SourceCharacterId == 0 || files.All(f => f.CharacterId != Settings.SourceCharacterId))
            Settings.SourceCharacterId = files.OrderByDescending(f => f.LastWriteUtc).FirstOrDefault()?.CharacterId ?? 0;

        foreach (var file in files.OrderBy(f => NameFor(f.CharacterId), StringComparer.OrdinalIgnoreCase))
        {
            var row = new SettingsSyncRowViewModel(file, NameFor(file.CharacterId),
                isSource: file.CharacterId == Settings.SourceCharacterId,
                included: !Settings.ExcludedCharacterIds.Contains(file.CharacterId));
            row.Changed += OnRowChanged;
            Rows.Add(row);
        }

        Raise(nameof(CanSync));
        _ = ResolveUnknownNamesAsync();
    }

    private string NameFor(long id) => _names.TryGetValue(id, out var name) ? name : $"#{id}";

    private void OnRowChanged(SettingsSyncRowViewModel row, bool sourceChanged)
    {
        if (sourceChanged && row.IsSource)
        {
            Settings.SourceCharacterId = row.CharacterId;
            foreach (var other in Rows.Where(r => r != row))
                other.IsSource = false;
        }

        Settings.ExcludedCharacterIds = Rows.Where(r => !r.Included).Select(r => r.CharacterId).ToList();
        foreach (var r in Rows)
            r.Refresh();
        Raise(nameof(CanSync));
    }

    /// <summary>
    /// Settings files are named by id alone. Most ids are already known from the logs; the
    /// rest - alts not logged in this week - are named through ESI's public names endpoint.
    /// </summary>
    private async Task ResolveUnknownNamesAsync()
    {
        var unknown = Rows.Where(r => !_names.ContainsKey(r.CharacterId)).Select(r => r.CharacterId).ToList();
        if (unknown.Count == 0)
            return;

        IReadOnlyDictionary<long, string> resolved;
        try
        {
            using var esi = new EsiClient();
            resolved = await esi.ResolveCharacterNamesAsync(unknown);
        }
        catch (Exception)
        {
            // Offline or ESI down: the bare id is still a usable label.
            return;
        }

        foreach (var (id, name) in resolved)
        {
            _names[id] = name;
            _config.CharacterIds[name] = id;
            foreach (var row in Rows.Where(r => r.CharacterId == id))
                row.Name = name;
        }
    }

    public void SyncNow()
    {
        try
        {
            var result = _service.SyncNow();
            Message = result.Copied.Count == 0
                ? "Nothing to copy."
                : $"Copied {NameFor(Settings.SourceCharacterId)}'s layout to {result.Copied.Count} character(s). The originals are backed up.";
        }
        catch (Exception e)
        {
            Message = e.Message;
        }

        LoadRows();
        Raise(nameof(LastSyncText));
    }

    /// <summary>Puts back the files from before the most recent sync of this profile.</summary>
    public void RestoreLatest()
    {
        if (_selectedProfile is null)
            return;

        if (SettingsSyncService.OpenClients() > 0)
        {
            Message = "Close every EVE client before restoring.";
            return;
        }

        var sync = _service.CreateSync();
        var backup = sync.ListBackups().FirstOrDefault(b =>
            b.ProfilePath.Equals(_selectedProfile.Path, StringComparison.OrdinalIgnoreCase));
        if (backup is null)
        {
            Message = "No backup of this profile yet — one is taken before every sync.";
            return;
        }

        try
        {
            var restored = sync.Restore(backup);
            Message = $"Restored {restored} character file(s) from {backup.CreatedUtc.ToLocalTime():ddd d MMM HH:mm}.";
        }
        catch (Exception e)
        {
            Message = "Restore failed: " + e.Message;
        }

        LoadRows();
    }
}

/// <summary>One character's settings file in the sync list.</summary>
public sealed class SettingsSyncRowViewModel : ObservableObject
{
    public SettingsSyncRowViewModel(CharacterSettingsFile file, string name, bool isSource, bool included)
    {
        CharacterId = file.CharacterId;
        _name = name;
        _isSource = isSource;
        _included = included;
        LastChanged = file.LastWriteUtc.ToLocalTime().ToString("ddd d MMM HH:mm");
    }

    /// <summary>The row, and whether the source radio was what moved.</summary>
    public event Action<SettingsSyncRowViewModel, bool>? Changed;

    public long CharacterId { get; }
    public string IdText => CharacterId.ToString();
    public string LastChanged { get; }

    private string _name;
    public string Name { get => _name; set => Set(ref _name, value); }

    private bool _isSource;
    public bool IsSource
    {
        get => _isSource;
        set
        {
            if (Set(ref _isSource, value) && value)
                Changed?.Invoke(this, true);
        }
    }

    private bool _included;
    public bool Included
    {
        get => _included;
        set
        {
            if (Set(ref _included, value))
                Changed?.Invoke(this, false);
        }
    }

    public bool IsTarget => !IsSource && Included;

    public string RoleText => IsSource ? "SOURCE" : Included ? "WILL BE OVERWRITTEN" : "LEFT ALONE";

    public Brush RoleBrush => IsSource ? Palette.NeutBlue : Included ? Palette.Gold : Palette.TextMuted;

    public void Refresh()
    {
        Raise(nameof(IsTarget));
        Raise(nameof(RoleText));
        Raise(nameof(RoleBrush));
    }
}

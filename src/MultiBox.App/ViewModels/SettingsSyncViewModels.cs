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

        // Most recently written first: a client rewrites its profile's files at every logout,
        // so the top of the list is the profile the launcher is actually using.
        foreach (var profile in EveSettingsLocator.FindProfiles()
                     .OrderByDescending(p => p.LastWriteUtc ?? DateTime.MinValue))
            Profiles.Add(new ProfileOptionViewModel(profile));

        _selectedProfile = Profiles.FirstOrDefault(p => p.Path.Equals(Settings.ProfilePath, StringComparison.OrdinalIgnoreCase))
                           ?? Profiles.FirstOrDefault();
        if (_selectedProfile is not null)
            Settings.ProfilePath = _selectedProfile.Path;

        LoadOtherProfiles();
        LoadRows();
        RefreshClients();
    }

    private SettingsSyncConfig Settings => _config.SettingsSync;

    public ObservableCollection<ProfileOptionViewModel> Profiles { get; } = new();

    /// <summary>Every profile except the selected one, each with an "also apply" tick.</summary>
    public ObservableCollection<AlsoApplyProfileViewModel> OtherProfiles { get; } = new();

    public bool HasOtherProfiles => OtherProfiles.Count > 0;
    public ObservableCollection<SettingsSyncRowViewModel> Rows { get; } = new();

    public bool HasProfiles => Profiles.Count > 0;

    private ProfileOptionViewModel? _selectedProfile;
    public ProfileOptionViewModel? SelectedProfile
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
            Settings.AlsoApplyToProfilePaths.RemoveAll(p => p.Equals(value.Path, StringComparison.OrdinalIgnoreCase));
            LoadOtherProfiles();
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

    // From the plan the sync will run, so SYNC NOW is never offered for a sync with nothing to
    // copy (for instance every ticked profile skipped and every row unticked).
    public bool CanSync => _openClients == 0 && Settings.IsConfigured &&
                           SettingsSyncPlanner.BuildFromDisk(Settings).TargetCount > 0;

    /// <summary>How many more files the ticked profiles add, so the reach of a sync is visible.</summary>
    public string AlsoApplySummary
    {
        get
        {
            var ticked = OtherProfiles.Where(p => p.IsChecked).ToList();
            if (ticked.Count == 0)
                return "Only the profile above is changed.";

            // The same plan the sync runs, so this line can't promise something it won't do.
            var plan = SettingsSyncPlanner.BuildFromDisk(Settings);
            var others = plan.Steps.Skip(1).ToList();
            var files = others.Sum(s => s.Targets.Count);

            var text = CopyInto
                ? $"Copies this profile's layout over {files} character file(s) in {others.Count} other profile(s), " +
                  "including the source character's own copy there."
                : $"Syncs {others.Count} other profile(s) within themselves, {files} character file(s) in all, " +
                  $"each from {NameFor(Settings.SourceCharacterId)}'s own copy there. Each profile keeps its own layout.";

            if (plan.SkippedProfiles.Count > 0)
                text += " Skipped, because they hold no copy of the source character's settings: " +
                        string.Join(", ", plan.SkippedProfiles.Select(ProfileName)) + ".";

            return text + " Unticked characters are left alone in every profile.";
        }
    }

    /// <summary>The ticked profiles all get this profile's layout.</summary>
    public bool CopyInto
    {
        get => Settings.Mode == SettingsSyncMode.CopyInto;
        set { if (value) SetMode(SettingsSyncMode.CopyInto); }
    }

    /// <summary>Each ticked profile keeps its own layout and is synced within itself.</summary>
    public bool WithinEach
    {
        get => Settings.Mode == SettingsSyncMode.WithinEach;
        set { if (value) SetMode(SettingsSyncMode.WithinEach); }
    }

    private void SetMode(SettingsSyncMode mode)
    {
        if (Settings.Mode == mode)
            return;

        Settings.Mode = mode;
        Raise(nameof(CopyInto));
        Raise(nameof(WithinEach));
        Raise(nameof(AlsoApplySummary));
        Raise(nameof(CanSync));
    }

    private string ProfileName(string path) =>
        Profiles.FirstOrDefault(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase))?.Name
        ?? System.IO.Path.GetFileName(path);

    private void LoadOtherProfiles()
    {
        OtherProfiles.Clear();
        foreach (var option in Profiles.Where(p => p != _selectedProfile))
        {
            var row = new AlsoApplyProfileViewModel(option,
                Settings.AlsoApplyToProfilePaths.Contains(option.Path, StringComparer.OrdinalIgnoreCase));
            row.Changed += OnAlsoApplyChanged;
            OtherProfiles.Add(row);
        }

        Raise(nameof(HasOtherProfiles));
        Raise(nameof(AlsoApplySummary));
    }

    private void OnAlsoApplyChanged()
    {
        Settings.AlsoApplyToProfilePaths = OtherProfiles.Where(p => p.IsChecked).Select(p => p.Path).ToList();
        Raise(nameof(AlsoApplySummary));
        Raise(nameof(CanSync));
    }

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
        Raise(nameof(AlsoApplySummary));
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

        // The summary names the source character, which may only now have a name.
        Raise(nameof(AlsoApplySummary));
    }

    public void SyncNow()
    {
        try
        {
            var result = _service.SyncNow();
            Message = result.Copied.Count == 0
                ? "Nothing to copy."
                : $"Synced {NameFor(Settings.SourceCharacterId)}'s layout to {result.Copied.Count} character file(s). The originals are backed up.";
        }
        catch (Exception e)
        {
            Message = e.Message;
        }

        RefreshProfiles();
        LoadRows();
        Raise(nameof(LastSyncText));
    }

    /// <summary>Write times move after a sync or restore, and the labels show them.</summary>
    private void RefreshProfiles()
    {
        var fresh = EveSettingsLocator.FindProfiles().ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var option in Profiles)
            if (fresh.TryGetValue(option.Path, out var profile))
                option.Update(profile);
        Raise(nameof(AlsoApplySummary));
    }

    /// <summary>Puts back every profile the most recent sync touched, as it was before.</summary>
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
        var batch = sync.LatestBatch(Settings.OtherProfiles().Append(_selectedProfile.Path));
        if (batch.Count == 0)
        {
            Message = "No backup of these profiles yet — one is taken before every sync.";
            return;
        }

        try
        {
            var restored = batch.Sum(sync.Restore);
            Message = $"Restored {restored} character file(s) in {batch.Count} profile(s) from " +
                      $"{batch.Max(b => b.CreatedUtc).ToLocalTime():ddd d MMM HH:mm}.";
        }
        catch (Exception e)
        {
            Message = "Restore failed: " + e.Message;
        }

        RefreshProfiles();
        LoadRows();
    }
}

/// <summary>A settings profile as the picker shows it: name, size and when it was last written.</summary>
public sealed class ProfileOptionViewModel : ObservableObject
{
    private SettingsProfile _profile;

    public ProfileOptionViewModel(SettingsProfile profile) => _profile = profile;

    public string Path => _profile.Path;
    public string Name => _profile.Name;

    /// <summary>"Split_Screen · 12 character(s) · last written today 21:00".</summary>
    public string Label
    {
        get
        {
            var written = _profile.LastWriteUtc is { } utc ? "last written " + When(utc.ToLocalTime()) : "never used";
            return $"{_profile.Name}  ·  {_profile.CharacterCount} character(s)  ·  {written}";
        }
    }

    public void Update(SettingsProfile profile)
    {
        _profile = profile;
        Raise(nameof(Label));
    }

    public override string ToString() => Label;

    private static string When(DateTime local)
    {
        var days = (DateTime.Today - local.Date).Days;
        return days switch
        {
            0 => "today " + local.ToString("HH:mm"),
            1 => "yesterday " + local.ToString("HH:mm"),
            < 300 => local.ToString("d MMM HH:mm"),
            _ => local.ToString("d MMM yyyy")
        };
    }
}

/// <summary>Another profile that can also receive the source layout.</summary>
public sealed class AlsoApplyProfileViewModel : ObservableObject
{
    private readonly ProfileOptionViewModel _option;

    public AlsoApplyProfileViewModel(ProfileOptionViewModel option, bool isChecked)
    {
        _option = option;
        _isChecked = isChecked;
        _option.PropertyChanged += (_, _) => Raise(nameof(Label));
    }

    public event Action? Changed;

    public string Path => _option.Path;
    public string Label => _option.Label;

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value))
                Changed?.Invoke();
        }
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

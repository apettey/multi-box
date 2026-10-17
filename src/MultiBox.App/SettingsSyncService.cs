using System.IO;
using MultiBox.App.Interop;
using MultiBox.Core.Config;
using MultiBox.Core.Settings;

namespace MultiBox.App;

/// <summary>
/// Runs settings sync on demand and, when switched on, by itself once every client has
/// closed. The safety rule lives here in one place: nothing is copied while any EVE window
/// is open, because the client would write its own copy back over ours on logout.
/// </summary>
public sealed class SettingsSyncService
{
    private readonly MultiBoxConfig _config;
    private readonly Action _saveConfig;
    private readonly AutoSyncGate _gate = new();

    /// <summary>The newest write time an automatic attempt failed on, so a broken copy is not retried every tick.</summary>
    private DateTime? _failedOn;

    public SettingsSyncService(MultiBoxConfig config, Action saveConfig)
    {
        _config = config;
        _saveConfig = saveConfig;
    }

    public SettingsSyncConfig Settings => _config.SettingsSync;

    /// <summary>Raised after any sync, automatic or manual, with a line describing it.</summary>
    public event Action<string>? Synced;

    /// <summary>Set while the sync window is open, so the choices are not acted on half-made.</summary>
    public bool Paused { get; set; }

    public CharacterSettingsSync CreateSync() =>
        new(CharacterSettingsSync.DefaultBackupRoot, Settings.KeepBackups);

    public static int OpenClients()
    {
        try
        {
            return EveClientLocator.CountOpenClients();
        }
        catch (Exception)
        {
            // If windows cannot be counted, assume the worst rather than copy under a client.
            return int.MaxValue;
        }
    }

    /// <summary>Called on the dashboard's rescan timer.</summary>
    public void Tick()
    {
        if (!Settings.AutoSync || !Settings.IsConfigured || Paused)
            return;

        IReadOnlyList<CharacterSettingsFile> files;
        try
        {
            files = EveSettingsLocator.CharacterFiles(Settings.ProfilePath!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (files.All(f => f.CharacterId != Settings.SourceCharacterId))
            return;

        var involved = Settings.TargetsAmong(files.Select(f => f.CharacterId)).Append(Settings.SourceCharacterId).ToHashSet();
        var newest = files.Where(f => involved.Contains(f.CharacterId)).Max(f => f.LastWriteUtc);
        if (newest == _failedOn)
            return;

        if (!_gate.ShouldSync(DateTime.UtcNow, OpenClients(), newest, Settings.LastSyncUtc))
            return;

        try
        {
            SyncNow(automatic: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _failedOn = newest;
            Synced?.Invoke("Settings sync failed: " + e.Message);
        }
    }

    /// <summary>Copies the source layout over every target. Refuses while any client is open.</summary>
    public SyncResult SyncNow(bool automatic = false)
    {
        if (!Settings.IsConfigured)
            throw new InvalidOperationException("Choose a profile and a source character first.");

        var open = OpenClients();
        if (open > 0)
            throw new InvalidOperationException(
                "Close every EVE client first - a running client writes its own settings back when it logs out.");

        var profile = Settings.ProfilePath!;
        var available = EveSettingsLocator.CharacterFiles(profile).Select(f => f.CharacterId);
        var result = CreateSync().Apply(profile, Settings.SourceCharacterId, Settings.TargetsAmong(available));

        Settings.LastSyncUtc = DateTime.UtcNow;
        _failedOn = null;
        _saveConfig();

        var how = automatic ? "after the last client closed" : "on request";
        Synced?.Invoke(result.Copied.Count == 0
            ? $"Settings sync {how}: nothing to copy."
            : $"Synced layout to {result.Copied.Count} character(s) {how}.");
        return result;
    }
}

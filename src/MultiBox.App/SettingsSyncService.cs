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

        DateTime newest;
        try
        {
            var plan = SettingsSyncPlanner.BuildFromDisk(Settings);
            if (SettingsSyncPlanner.NewestWrite(plan, Settings.SourceCharacterId) is not { } written)
                return;
            newest = written;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

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

    /// <summary>
    /// Copies the source layout over every target in the main profile and in each "also apply
    /// to" profile. Refuses while any client is open. Every profile touched is backed up under
    /// one batch, so RESTORE LAST BACKUP undoes the whole sync.
    /// </summary>
    public SyncResult SyncNow(bool automatic = false)
    {
        if (!Settings.IsConfigured)
            throw new InvalidOperationException("Choose a profile and a source character first.");

        var open = OpenClients();
        if (open > 0)
            throw new InvalidOperationException(
                "Close every EVE client first - a running client writes its own settings back when it logs out.");

        var plan = SettingsSyncPlanner.BuildFromDisk(Settings);
        if (plan.Steps.Count == 0)
            throw new InvalidOperationException("The source character has no settings file in the selected profile.");

        var result = SettingsSyncPlanner.Run(plan, Settings.SourceCharacterId, CreateSync(), Guid.NewGuid());
        var profiles = plan.Steps.Count(s => s.Targets.Count > 0);

        Settings.LastSyncUtc = DateTime.UtcNow;
        _failedOn = null;
        _saveConfig();

        var how = automatic ? "after the last client closed" : "on request";
        var skipped = plan.SkippedProfiles.Count == 0
            ? string.Empty
            : $" Skipped {plan.SkippedProfiles.Count} profile(s) the source character has never logged in with.";
        Synced?.Invoke((result.Copied.Count == 0
            ? $"Settings sync {how}: nothing to copy."
            : $"Synced layout to {result.Copied.Count} character file(s) in {profiles} profile(s) {how}.") + skipped);
        return result;
    }
}

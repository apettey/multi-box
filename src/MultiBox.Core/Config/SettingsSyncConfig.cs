namespace MultiBox.Core.Config;

/// <summary>
/// Settings sync: which character's EVE layout is copied onto the rest.
///
/// Targets are stored as an exclusion list rather than an inclusion list, so an alt that logs
/// in for the first time is synced along with everyone else instead of silently left out.
/// </summary>
public sealed class SettingsSyncConfig
{
    /// <summary>Full path of the <c>settings_&lt;Profile&gt;</c> folder. Null until one is chosen.</summary>
    public string? ProfilePath { get; set; }

    /// <summary>Character whose layout is copied. Zero means none chosen, and nothing syncs.</summary>
    public long SourceCharacterId { get; set; }

    /// <summary>Characters left alone, for an alt that deliberately keeps its own layout.</summary>
    public List<long> ExcludedCharacterIds { get; set; } = new();

    /// <summary>Sync automatically once every client has closed. Off until switched on.</summary>
    public bool AutoSync { get; set; }

    /// <summary>Backup zips kept before the oldest is deleted.</summary>
    public int KeepBackups { get; set; } = 10;

    public DateTime? LastSyncUtc { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProfilePath) && SourceCharacterId > 0;

    /// <summary>Every id in <paramref name="available"/> except the source and the excluded.</summary>
    public IReadOnlyList<long> TargetsAmong(IEnumerable<long> available) =>
        available.Where(id => id != SourceCharacterId && !ExcludedCharacterIds.Contains(id)).ToList();
}

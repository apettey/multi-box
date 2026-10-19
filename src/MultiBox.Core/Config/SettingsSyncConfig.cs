using System.Text.Json.Serialization;
using MultiBox.Core.Settings;

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

    /// <summary>
    /// Other <c>settings_&lt;Profile&gt;</c> folders that also receive the source layout. The
    /// launcher assigns a profile per account, so a fleet split across accounts can read its
    /// settings from several folders; each character's file in each of these is overwritten.
    /// </summary>
    public List<string> AlsoApplyToProfilePaths { get; set; } = new();

    /// <summary>
    /// Whether the ticked profiles receive the selected profile's layout (<c>CopyInto</c>), or
    /// are each synced within themselves from the source character's copy there
    /// (<c>WithinEach</c>), keeping a different layout per profile. Written as its name.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SettingsSyncMode>))]
    public SettingsSyncMode Mode { get; set; } = SettingsSyncMode.CopyInto;

    /// <summary>Characters left alone, for an alt that deliberately keeps its own layout.</summary>
    public List<long> ExcludedCharacterIds { get; set; } = new();

    /// <summary>Sync automatically once every client has closed. Off until switched on.</summary>
    public bool AutoSync { get; set; }

    /// <summary>Backup zips kept before the oldest is deleted.</summary>
    public int KeepBackups { get; set; } = 10;

    public DateTime? LastSyncUtc { get; set; }

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProfilePath) && SourceCharacterId > 0;

    /// <summary>Every id in <paramref name="available"/> except the source and the excluded.</summary>
    public IReadOnlyList<long> TargetsAmong(IEnumerable<long> available) =>
        available.Where(id => id != SourceCharacterId && !ExcludedCharacterIds.Contains(id)).ToList();

    /// <summary>
    /// Targets in another profile. The source character is one of them: its file there is a
    /// separate copy that the launcher may be using, and it should get the layout too.
    /// </summary>
    public IReadOnlyList<long> TargetsInOtherProfile(IEnumerable<long> available) =>
        available.Where(id => !ExcludedCharacterIds.Contains(id)).ToList();

    /// <summary>The profiles to apply to besides the main one, without duplicates or the main itself.</summary>
    public IReadOnlyList<string> OtherProfiles() =>
        AlsoApplyToProfilePaths
            .Where(p => !string.IsNullOrWhiteSpace(p) && !p.Equals(ProfilePath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

using System.Text.RegularExpressions;

namespace MultiBox.Core.Settings;

/// <summary>
/// One <c>settings_&lt;Profile&gt;</c> folder: the set of settings files a launcher profile
/// points the client at. Every character and account that launches with this profile reads
/// and writes its files here.
/// </summary>
public sealed record SettingsProfile(string ServerFolder, string Name, string Path,
    int CharacterCount = 0, DateTime? LastWriteUtc = null)
{
    /// <summary>"c_eve_sharedcache_tq_tranquility / Default" - enough to tell two installs apart.</summary>
    public string DisplayName => $"{ServerFolder} / {Name}";
}

/// <summary>
/// A <c>core_char_&lt;characterID&gt;.dat</c> file. The id is the same ESI character id that
/// names the log files, which is what lets a settings file be shown by pilot name.
/// </summary>
public sealed record CharacterSettingsFile(long CharacterId, string Path, DateTime LastWriteUtc);

/// <summary>
/// Finds EVE's per-character settings files. Nothing here opens a file; it lists names and
/// timestamps only.
///
/// Layout, from the client's own folders rather than any CCP documentation:
/// <c>%LOCALAPPDATA%\CCP\EVE\&lt;install&gt;_tq_tranquility\settings_&lt;Profile&gt;\core_char_&lt;id&gt;.dat</c>.
/// The first folder's name is derived from the install path, so it is matched by its server
/// suffix rather than by a fixed name.
/// </summary>
public static class EveSettingsLocator
{
    /// <summary>Matches only real per-character files: <c>core_char__.dat</c> is a shared default.</summary>
    private static readonly Regex CharFile = new(@"^core_char_(?<id>\d+)\.dat$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string DefaultRoot =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP", "EVE");

    /// <summary>
    /// Tranquility profiles only. Singularity and the other test servers have no public ESI
    /// to name their characters, and syncing a test-server layout onto live alts is never
    /// what anyone wants.
    /// </summary>
    public static IReadOnlyList<SettingsProfile> FindProfiles(string? root = null)
    {
        root ??= DefaultRoot;
        if (!Directory.Exists(root))
            return Array.Empty<SettingsProfile>();

        var profiles = new List<SettingsProfile>();
        foreach (var server in Directory.EnumerateDirectories(root))
        {
            var serverName = System.IO.Path.GetFileName(server);
            if (!serverName.Contains("tranquility", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var profile in Directory.EnumerateDirectories(server, "settings_*"))
            {
                var name = System.IO.Path.GetFileName(profile)["settings_".Length..];
                if (name.Length == 0)
                    continue;

                // The newest character file is the best local hint of which profile the
                // launcher is actually using: a client writes it at every logout.
                var files = CharacterFiles(profile);
                profiles.Add(new SettingsProfile(serverName, name, profile, files.Count,
                    files.Count == 0 ? null : files.Max(f => f.LastWriteUtc)));
            }
        }

        return profiles
            .OrderBy(p => p.ServerFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name.Equals("Default", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<CharacterSettingsFile> CharacterFiles(string profilePath)
    {
        if (!Directory.Exists(profilePath))
            return Array.Empty<CharacterSettingsFile>();

        var files = new List<CharacterSettingsFile>();
        foreach (var path in Directory.EnumerateFiles(profilePath, "core_char_*.dat"))
        {
            var id = TryParseCharacterId(System.IO.Path.GetFileName(path));
            if (id is not null)
                files.Add(new CharacterSettingsFile(id.Value, path, File.GetLastWriteTimeUtc(path)));
        }

        return files.OrderBy(f => f.CharacterId).ToList();
    }

    /// <summary>The character id in a <c>core_char_&lt;id&gt;.dat</c> name, or null for anything else.</summary>
    public static long? TryParseCharacterId(string fileName)
    {
        var m = CharFile.Match(fileName);
        return m.Success && long.TryParse(m.Groups["id"].Value, out var id) && id > 0 ? id : null;
    }
}

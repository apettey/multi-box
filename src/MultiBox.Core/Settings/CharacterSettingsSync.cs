using System.IO.Compression;
using System.Text.Json;

namespace MultiBox.Core.Settings;

/// <summary>What one sync did: where the safety copy went and which characters and accounts were overwritten.</summary>
public sealed record SyncResult(string? BackupPath, IReadOnlyList<long> Copied)
{
    public static readonly SyncResult Nothing = new(null, Array.Empty<long>());

    /// <summary>Accounts overwritten by the same sync, when account settings were part of it.</summary>
    public IReadOnlyList<long> AccountsCopied { get; init; } = Array.Empty<long>();
}

/// <summary>
/// One zip of a profile's character files or of its account files, taken before a sync
/// overwrote them. A sync takes one zip per profile for each kind it copies, all sharing a
/// <see cref="BatchId"/>, so they can be restored together.
/// </summary>
public sealed record SettingsBackup(string Path, string ProfilePath, DateTime CreatedUtc, int FileCount, Guid? BatchId = null);

/// <summary>
/// Copies one character's settings file over others, so every client opens with the same window
/// layout, and when asked one account's settings file over others, for the same keyboard shortcuts.
///
/// This is the one place MultiBox writes into a folder EVE owns, so the boundary is drawn
/// tightly and enforced here rather than by the caller:
/// <list type="bullet">
/// <item>Only <c>core_char_&lt;id&gt;.dat</c> and, when the player opts in, <c>core_user_&lt;id&gt;.dat</c>
/// files are ever written. Target paths are built from a numeric id, never taken from input, so
/// nothing else in the folder can be addressed.</item>
/// <item>Files are copied whole, byte for byte. Their contents are never parsed or edited.</item>
/// <item>Only files that already exist are overwritten: a character or account that has never
/// logged in gets no file conjured for it.</item>
/// <item>Every file of the kind being copied is zipped to MultiBox's own folder first.</item>
/// </list>
/// The caller must only run this while no EVE client is open. A running client holds its
/// settings in memory and writes them back on logout, which would quietly undo the copy.
/// </summary>
public sealed class CharacterSettingsSync
{
    private const string ManifestEntry = "multibox-backup.json";
    private const string TempSuffix = ".multibox-tmp";

    private readonly string _backupRoot;
    private readonly int _keepBackups;

    public CharacterSettingsSync(string backupRoot, int keepBackups = 10)
    {
        _backupRoot = backupRoot;
        _keepBackups = Math.Max(1, keepBackups);
    }

    public static string DefaultBackupRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MultiBox", "settings-backups");

    /// <summary>
    /// Overwrites each target's file with the source's. Targets without a file, and the source
    /// itself, are skipped. Nothing is backed up or written when there is nothing to copy.
    /// </summary>
    public SyncResult Apply(string profilePath, long sourceId, IEnumerable<long> targetIds, Guid? batchId = null) =>
        Apply(profilePath, sourceId, profilePath, targetIds, batchId);

    /// <summary>
    /// Copies the source character's file from <paramref name="sourceProfile"/> over each
    /// target's file in <paramref name="targetProfile"/>. When the two differ the source
    /// character's own file in the target profile is a valid target too.
    /// </summary>
    public SyncResult Apply(string sourceProfile, long sourceId, string targetProfile,
        IEnumerable<long> targetIds, Guid? batchId = null)
    {
        var source = CharacterFilePath(sourceProfile, sourceId);
        if (!File.Exists(source))
            throw new FileNotFoundException("The source character has no settings file in this profile.", source);

        var sameProfile = Path.GetFullPath(sourceProfile).Equals(Path.GetFullPath(targetProfile), StringComparison.OrdinalIgnoreCase);
        var targets = targetIds
            .Where(id => !sameProfile || id != sourceId)
            .Distinct()
            .Where(id => File.Exists(CharacterFilePath(targetProfile, id)))
            .ToList();

        if (targets.Count == 0)
            return SyncResult.Nothing;

        var backup = Backup(targetProfile, batchId);

        var copied = new List<long>();
        foreach (var id in targets)
        {
            CopyOver(source, CharacterFilePath(targetProfile, id));
            copied.Add(id);
        }

        Prune();
        return new SyncResult(backup, copied);
    }

    /// <summary>
    /// Copies the source account's file from <paramref name="sourceProfile"/> over each target
    /// account's file in <paramref name="targetProfile"/>, under the same rules as characters.
    /// </summary>
    public SyncResult ApplyAccounts(string sourceProfile, long sourceId, string targetProfile,
        IEnumerable<long> targetIds, Guid? batchId = null)
    {
        var source = AccountFilePath(sourceProfile, sourceId);
        if (!File.Exists(source))
            throw new FileNotFoundException("The source account has no settings file in this profile.", source);

        var sameProfile = Path.GetFullPath(sourceProfile).Equals(Path.GetFullPath(targetProfile), StringComparison.OrdinalIgnoreCase);
        var targets = targetIds
            .Where(id => !sameProfile || id != sourceId)
            .Distinct()
            .Where(id => File.Exists(AccountFilePath(targetProfile, id)))
            .ToList();

        if (targets.Count == 0)
            return SyncResult.Nothing;

        var backup = BackupAccounts(targetProfile, batchId);

        var copied = new List<long>();
        foreach (var id in targets)
        {
            CopyOver(source, AccountFilePath(targetProfile, id));
            copied.Add(id);
        }

        Prune();
        return new SyncResult(backup, Array.Empty<long>()) { AccountsCopied = copied };
    }

    private static void CopyOver(string source, string target)
    {
        var temp = target + TempSuffix;
        try
        {
            // Copy beside the target and then swap, so an interrupted copy leaves the old
            // file in place rather than half of the new one.
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    /// <summary>Zips every character file in the profile into the backup folder.</summary>
    public string Backup(string profilePath, Guid? batchId = null) =>
        WriteZip(profilePath, EveSettingsLocator.CharacterFiles(profilePath).Select(f => f.Path).ToList(), batchId);

    /// <summary>
    /// Zips every account file in the profile into the backup folder. A separate zip from the
    /// character files: a sync's account step runs after its character step, so one zip of both
    /// would capture character files that had already been overwritten.
    /// </summary>
    public string BackupAccounts(string profilePath, Guid? batchId = null) =>
        WriteZip(profilePath, EveSettingsLocator.AccountFiles(profilePath).Select(f => f.Path).ToList(), batchId);

    private string WriteZip(string profilePath, IReadOnlyList<string> files, Guid? batchId)
    {
        Directory.CreateDirectory(_backupRoot);

        var created = DateTime.UtcNow;
        var profileName = Path.GetFileName(Path.TrimEndingDirectorySeparator(profilePath));
        // The random tail keeps two profiles backed up in the same millisecond apart.
        var tail = Guid.NewGuid().ToString("N")[..6];
        var path = Path.Combine(_backupRoot, $"{created:yyyyMMdd-HHmmss-fff}_{Sanitise(profileName)}_{tail}.zip");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var file in files)
                zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);

            var manifest = zip.CreateEntry(ManifestEntry);
            using var stream = manifest.Open();
            JsonSerializer.Serialize(stream, new Manifest(profilePath, created, files.Count, batchId));
        }

        return path;
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<SettingsBackup> ListBackups()
    {
        if (!Directory.Exists(_backupRoot))
            return Array.Empty<SettingsBackup>();

        var backups = new List<SettingsBackup>();
        foreach (var path in Directory.EnumerateFiles(_backupRoot, "*.zip"))
        {
            var manifest = TryReadManifest(path);
            if (manifest is not null)
                backups.Add(new SettingsBackup(path, manifest.ProfilePath, manifest.CreatedUtc, manifest.FileCount, manifest.BatchId));
        }

        return backups.OrderByDescending(b => b.CreatedUtc).ToList();
    }

    /// <summary>
    /// Puts a backup's files back into the profile it was taken from. Entries that are not
    /// <c>core_char_&lt;id&gt;.dat</c> or <c>core_user_&lt;id&gt;.dat</c> are ignored, so a backup can only
    /// ever restore the kind of file a sync could have changed.
    /// </summary>
    public int Restore(SettingsBackup backup)
    {
        if (!Directory.Exists(backup.ProfilePath))
            throw new DirectoryNotFoundException($"The profile this backup came from no longer exists: {backup.ProfilePath}");

        var restored = 0;
        using var zip = ZipFile.OpenRead(backup.Path);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName != entry.Name)
                continue;

            string target;
            if (EveSettingsLocator.TryParseCharacterId(entry.Name) is { } characterId)
                target = CharacterFilePath(backup.ProfilePath, characterId);
            else if (EveSettingsLocator.TryParseAccountId(entry.Name) is { } accountId)
                target = AccountFilePath(backup.ProfilePath, accountId);
            else
                continue;

            var temp = target + TempSuffix;
            try
            {
                entry.ExtractToFile(temp, overwrite: true);
                File.Move(temp, target, overwrite: true);
                restored++;
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        return restored;
    }

    /// <summary>
    /// The backups taken by the most recent sync touching any of <paramref name="profilePaths"/>:
    /// every profile it covered, so restoring puts the whole sync back rather than one folder.
    /// </summary>
    public IReadOnlyList<SettingsBackup> LatestBatch(IEnumerable<string> profilePaths)
    {
        var wanted = profilePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = ListBackups();
        var newest = all.FirstOrDefault(b => wanted.Contains(b.ProfilePath));
        if (newest is null)
            return Array.Empty<SettingsBackup>();

        return newest.BatchId is { } batch
            ? all.Where(b => b.BatchId == batch).ToList()
            : new[] { newest };
    }

    /// <summary>One of the two shapes of path this class ever writes to.</summary>
    public static string CharacterFilePath(string profilePath, long characterId)
    {
        if (characterId <= 0)
            throw new ArgumentOutOfRangeException(nameof(characterId), "Character ids are positive.");
        return Path.Combine(profilePath, $"core_char_{characterId}.dat");
    }

    /// <summary>The other shape of path this class ever writes to.</summary>
    public static string AccountFilePath(string profilePath, long accountId)
    {
        if (accountId <= 0)
            throw new ArgumentOutOfRangeException(nameof(accountId), "Account ids are positive.");
        return Path.Combine(profilePath, $"core_user_{accountId}.dat");
    }

    private void Prune()
    {
        // Only zips this class wrote (they carry a manifest) are candidates for deletion. The
        // limit counts syncs, not zips, so a sync across three profiles is never half-kept.
        var stale = ListBackups()
            .GroupBy(b => b.BatchId?.ToString() ?? b.Path)
            .OrderByDescending(g => g.Max(b => b.CreatedUtc))
            .Skip(_keepBackups)
            .SelectMany(g => g);
        foreach (var old in stale)
            File.Delete(old.Path);
    }

    private static Manifest? TryReadManifest(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(ManifestEntry);
            if (entry is null)
                return null;
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<Manifest>(stream);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException)
        {
            return null;
        }
    }

    private static string Sanitise(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private sealed record Manifest(string ProfilePath, DateTime CreatedUtc, int FileCount, Guid? BatchId = null);
}

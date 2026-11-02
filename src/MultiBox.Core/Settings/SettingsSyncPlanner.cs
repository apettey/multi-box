using MultiBox.Core.Config;

namespace MultiBox.Core.Settings;

/// <summary>
/// How the profiles ticked under "also apply to" are treated.
/// </summary>
public enum SettingsSyncMode
{
    /// <summary>
    /// The source character's file in the selected profile is copied into every ticked profile,
    /// so they all end up with that one layout.
    /// </summary>
    CopyInto,

    /// <summary>
    /// Each ticked profile is synced within itself, from the source character's own copy in that
    /// profile. Profiles keep their different layouts (single screen, split screen...) and every
    /// character inside one shares it. A profile the source character has never logged in with
    /// has nothing to copy from and is skipped.
    /// </summary>
    WithinEach,
}

/// <summary>One copy run: the source file in <see cref="SourceProfile"/> over <see cref="Targets"/> in <see cref="TargetProfile"/>.</summary>
public sealed record SyncStep(string SourceProfile, string TargetProfile, IReadOnlyList<long> Targets);

/// <summary>What a sync would do, and the ticked profiles it has to leave out.</summary>
public sealed record SyncPlan(IReadOnlyList<SyncStep> Steps, IReadOnlyList<string> SkippedProfiles)
{
    public static readonly SyncPlan Empty = new(Array.Empty<SyncStep>(), Array.Empty<string>());

    /// <summary>The same copy runs for account files, when account settings are part of the sync.</summary>
    public IReadOnlyList<SyncStep> AccountSteps { get; init; } = Array.Empty<SyncStep>();

    /// <summary>Ticked profiles left out of the account copy, having no copy of the source account.</summary>
    public IReadOnlyList<string> SkippedAccountProfiles { get; init; } = Array.Empty<string>();

    public long SourceAccountId { get; init; }

    /// <summary>Account files across every step.</summary>
    public int AccountTargetCount => AccountSteps.Sum(s => s.Targets.Count);

    /// <summary>Target files across every step: what a sync would overwrite.</summary>
    public int TargetCount => Steps.Sum(s => s.Targets.Count) + AccountTargetCount;
}

/// <summary>
/// Turns the sync settings into concrete copy runs. Kept apart from the copying so the rules of
/// both modes can be tested without touching a file, and so the window's summary line and the
/// sync itself can never disagree about what will happen.
/// </summary>
public static class SettingsSyncPlanner
{
    /// <param name="settings">Profile, source, exclusions, ticked profiles and mode.</param>
    /// <param name="characterIdsIn">Character ids with a settings file in a profile folder.</param>
    /// <param name="accountIdsIn">Account ids with a settings file in a profile folder.</param>
    public static SyncPlan Build(SettingsSyncConfig settings, Func<string, IReadOnlyCollection<long>> characterIdsIn,
        Func<string, IReadOnlyCollection<long>>? accountIdsIn = null)
    {
        if (!settings.IsConfigured)
            return SyncPlan.Empty;

        var main = settings.ProfilePath!;
        var source = settings.SourceCharacterId;
        var steps = new List<SyncStep>();
        var skipped = new List<string>();

        var inMain = characterIdsIn(main);
        if (!inMain.Contains(source))
            return SyncPlan.Empty;

        steps.Add(new SyncStep(main, main, settings.TargetsAmong(inMain)));

        foreach (var other in settings.OtherProfiles())
        {
            var ids = characterIdsIn(other);

            if (settings.Mode == SettingsSyncMode.CopyInto)
            {
                steps.Add(new SyncStep(main, other, settings.TargetsInOtherProfile(ids)));
                continue;
            }

            // Within each: that profile's own copy of the source is the layout to spread, so
            // without one there is nothing to sync from.
            if (!ids.Contains(source))
            {
                skipped.Add(other);
                continue;
            }

            steps.Add(new SyncStep(other, other, settings.TargetsAmong(ids)));
        }

        var plan = new SyncPlan(steps, skipped);
        return settings.CopiesAccountSettings && accountIdsIn is not null
            ? WithAccounts(plan, settings, accountIdsIn)
            : plan;
    }

    /// <summary>The account copy follows the same mode and the same ticked profiles as the characters.</summary>
    private static SyncPlan WithAccounts(SyncPlan plan, SettingsSyncConfig settings, Func<string, IReadOnlyCollection<long>> accountIdsIn)
    {
        var main = settings.ProfilePath!;
        var source = settings.SourceAccountId;

        var inMain = accountIdsIn(main);
        if (!inMain.Contains(source))
            return plan;

        var steps = new List<SyncStep> { new(main, main, settings.AccountTargetsAmong(inMain)) };
        var skipped = new List<string>();

        foreach (var other in settings.OtherProfiles())
        {
            var ids = accountIdsIn(other);

            if (settings.Mode == SettingsSyncMode.CopyInto)
                steps.Add(new SyncStep(main, other, settings.AccountTargetsInOtherProfile(ids)));
            else if (ids.Contains(source))
                steps.Add(new SyncStep(other, other, settings.AccountTargetsAmong(ids)));
            else
                skipped.Add(other);
        }

        return plan with { AccountSteps = steps, SkippedAccountProfiles = skipped, SourceAccountId = source };
    }

    /// <summary>The plan for the settings files actually on disk. A missing folder counts as empty.</summary>
    public static SyncPlan BuildFromDisk(SettingsSyncConfig settings) =>
        Build(settings,
            profile => EveSettingsLocator.CharacterFiles(profile).Select(f => f.CharacterId).ToHashSet(),
            profile => EveSettingsLocator.AccountFiles(profile).Select(f => f.AccountId).ToHashSet());

    /// <summary>
    /// Latest write time across every file the plan reads or overwrites, or null when it touches
    /// none. Auto-sync re-runs when this moves past the last sync: a layout changed on any
    /// character in any profile involved is a reason to sync again.
    /// </summary>
    public static DateTime? NewestWrite(SyncPlan plan, long sourceId)
    {
        DateTime? newest = null;
        foreach (var step in plan.Steps)
        {
            var paths = step.Targets
                .Select(id => CharacterSettingsSync.CharacterFilePath(step.TargetProfile, id))
                .Append(CharacterSettingsSync.CharacterFilePath(step.SourceProfile, sourceId));

            foreach (var path in paths.Where(File.Exists))
            {
                var written = File.GetLastWriteTimeUtc(path);
                if (newest is null || written > newest)
                    newest = written;
            }
        }

        foreach (var step in plan.AccountSteps)
        {
            var paths = step.Targets
                .Select(id => CharacterSettingsSync.AccountFilePath(step.TargetProfile, id))
                .Append(CharacterSettingsSync.AccountFilePath(step.SourceProfile, plan.SourceAccountId));

            foreach (var path in paths.Where(File.Exists))
            {
                var written = File.GetLastWriteTimeUtc(path);
                if (newest is null || written > newest)
                    newest = written;
            }
        }

        return newest;
    }

    /// <summary>
    /// Runs every step under one backup batch, so RESTORE LAST BACKUP puts all of them back.
    /// </summary>
    public static SyncResult Run(SyncPlan plan, long sourceId, CharacterSettingsSync sync, Guid batch)
    {
        string? firstBackup = null;
        var copied = new List<long>();

        foreach (var step in plan.Steps)
        {
            var result = sync.Apply(step.SourceProfile, sourceId, step.TargetProfile, step.Targets, batch);
            firstBackup ??= result.BackupPath;
            copied.AddRange(result.Copied);
        }

        var accounts = new List<long>();
        foreach (var step in plan.AccountSteps)
        {
            var result = sync.ApplyAccounts(step.SourceProfile, plan.SourceAccountId, step.TargetProfile, step.Targets, batch);
            firstBackup ??= result.BackupPath;
            accounts.AddRange(result.AccountsCopied);
        }

        return new SyncResult(firstBackup, copied) { AccountsCopied = accounts };
    }
}

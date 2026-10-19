using MultiBox.Core.Config;
using MultiBox.Core.Settings;
using Xunit;

namespace MultiBox.Core.Tests;

/// <summary>
/// The two ways ticked profiles are treated. Settings files are opaque binary, so these use
/// stand-in contents in a temporary EVE-shaped folder; the ids are the real pilots in samples/.
/// </summary>
public sealed class SettingsSyncPlannerTests : IDisposable
{
    private const long Commander = 1899648001;
    private const long Major = 1532110739;
    private const long Lieutenant = 1462945193;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"multibox_planner_{Guid.NewGuid():N}");
    private readonly string _backups;

    public SettingsSyncPlannerTests() => _backups = Path.Combine(_root, "backups");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Profile(string name, params (long Id, string Content)[] files)
    {
        var path = Path.Combine(_root, "CCP", "EVE", "c_eve_sharedcache_tq_tranquility", "settings_" + name);
        Directory.CreateDirectory(path);
        foreach (var (id, content) in files)
            File.WriteAllText(CharacterSettingsSync.CharacterFilePath(path, id), content);
        return path;
    }

    private static string Read(string profile, long id) =>
        File.ReadAllText(CharacterSettingsSync.CharacterFilePath(profile, id));

    private static SettingsSyncConfig Config(string main, SettingsSyncMode mode, params string[] others) => new()
    {
        ProfilePath = main,
        SourceCharacterId = Commander,
        Mode = mode,
        AlsoApplyToProfilePaths = others.ToList(),
    };

    private SyncResult Sync(SettingsSyncConfig config) =>
        SettingsSyncPlanner.Run(SettingsSyncPlanner.BuildFromDisk(config), Commander,
            new CharacterSettingsSync(_backups), Guid.NewGuid());

    [Fact]
    public void CopyIntoGivesEveryTickedProfileTheSelectedProfilesLayout()
    {
        var single = Profile("Single", (Commander, "single cmd"), (Major, "single major"));
        var split = Profile("Split", (Commander, "split cmd"), (Major, "split major"));

        Sync(Config(single, SettingsSyncMode.CopyInto, split));

        Assert.Equal("single cmd", Read(single, Major));
        Assert.Equal("single cmd", Read(split, Commander));
        Assert.Equal("single cmd", Read(split, Major));
    }

    [Fact]
    public void WithinEachKeepsADifferentLayoutPerProfile()
    {
        var single = Profile("Single", (Commander, "single cmd"), (Major, "single major"), (Lieutenant, "single lt"));
        var split = Profile("Split", (Commander, "split cmd"), (Major, "split major"), (Lieutenant, "split lt"));
        var mining = Profile("Mining", (Commander, "mining cmd"), (Major, "mining major"));
        var fleet = Profile("Fleet", (Commander, "fleet cmd"), (Lieutenant, "fleet lt"));

        var result = Sync(Config(single, SettingsSyncMode.WithinEach, split, mining, fleet));

        Assert.Equal(new[] { "single cmd", "single cmd", "single cmd" },
            new[] { Read(single, Commander), Read(single, Major), Read(single, Lieutenant) });
        Assert.Equal(new[] { "split cmd", "split cmd", "split cmd" },
            new[] { Read(split, Commander), Read(split, Major), Read(split, Lieutenant) });
        Assert.Equal("mining cmd", Read(mining, Major));
        Assert.Equal("fleet cmd", Read(fleet, Lieutenant));
        Assert.Equal(6, result.Copied.Count);
    }

    [Fact]
    public void WithinEachSkipsAProfileTheSourceHasNeverLoggedInWith()
    {
        var single = Profile("Single", (Commander, "single cmd"), (Major, "single major"));
        var alts = Profile("AltsOnly", (Major, "alts major"), (Lieutenant, "alts lt"));

        var plan = SettingsSyncPlanner.BuildFromDisk(Config(single, SettingsSyncMode.WithinEach, alts));
        SettingsSyncPlanner.Run(plan, Commander, new CharacterSettingsSync(_backups), Guid.NewGuid());

        Assert.Equal(new[] { alts }, plan.SkippedProfiles);
        Assert.Single(plan.Steps);
        Assert.Equal("alts major", Read(alts, Major));
        Assert.Equal("alts lt", Read(alts, Lieutenant));
        Assert.False(File.Exists(CharacterSettingsSync.CharacterFilePath(alts, Commander)));
    }

    [Fact]
    public void CopyIntoStillReachesAProfileTheSourceNeverUsed()
    {
        var single = Profile("Single", (Commander, "single cmd"));
        var alts = Profile("AltsOnly", (Major, "alts major"));

        var plan = SettingsSyncPlanner.BuildFromDisk(Config(single, SettingsSyncMode.CopyInto, alts));
        SettingsSyncPlanner.Run(plan, Commander, new CharacterSettingsSync(_backups), Guid.NewGuid());

        Assert.Empty(plan.SkippedProfiles);
        Assert.Equal("single cmd", Read(alts, Major));
    }

    [Theory]
    [InlineData(SettingsSyncMode.CopyInto)]
    [InlineData(SettingsSyncMode.WithinEach)]
    public void ExcludedCharactersAreLeftAloneInEveryProfile(SettingsSyncMode mode)
    {
        var single = Profile("Single", (Commander, "single cmd"), (Lieutenant, "single lt"));
        var split = Profile("Split", (Commander, "split cmd"), (Lieutenant, "split lt"));
        var config = Config(single, mode, split);
        config.ExcludedCharacterIds.Add(Lieutenant);

        Sync(config);

        Assert.Equal("single lt", Read(single, Lieutenant));
        Assert.Equal("split lt", Read(split, Lieutenant));
    }

    [Fact]
    public void WithinEachHandlesMoreThanTenCharactersPerProfile()
    {
        var alts = Enumerable.Range(1, 14).Select(i => (Id: 2_000_000_000L + i, Content: $"alt {i}")).ToArray();
        var single = Profile("Single", alts.Prepend((Commander, "single cmd")).ToArray());
        var split = Profile("Split", alts.Prepend((Commander, "split cmd")).ToArray());

        var result = Sync(Config(single, SettingsSyncMode.WithinEach, split));

        Assert.Equal(28, result.Copied.Count);
        Assert.All(alts, a => Assert.Equal("single cmd", Read(single, a.Id)));
        Assert.All(alts, a => Assert.Equal("split cmd", Read(split, a.Id)));
    }

    [Fact]
    public void NothingIsPlannedWithoutASourceInTheSelectedProfile()
    {
        var single = Profile("Single", (Major, "single major"));
        var split = Profile("Split", (Commander, "split cmd"), (Major, "split major"));

        var plan = SettingsSyncPlanner.BuildFromDisk(Config(single, SettingsSyncMode.WithinEach, split));

        Assert.Empty(plan.Steps);
        Assert.Equal(0, plan.TargetCount);
    }

    [Fact]
    public void AMissingProfileFolderIsSkippedNotFatal()
    {
        var single = Profile("Single", (Commander, "single cmd"), (Major, "single major"));
        var gone = Path.Combine(_root, "CCP", "EVE", "c_eve_sharedcache_tq_tranquility", "settings_Deleted");

        var within = SettingsSyncPlanner.BuildFromDisk(Config(single, SettingsSyncMode.WithinEach, gone));
        var copy = SettingsSyncPlanner.BuildFromDisk(Config(single, SettingsSyncMode.CopyInto, gone));

        Assert.Equal(new[] { gone }, within.SkippedProfiles);
        Assert.Equal(0, copy.Steps.Skip(1).Sum(s => s.Targets.Count));
        Sync(Config(single, SettingsSyncMode.CopyInto, gone));
        Assert.False(Directory.Exists(gone));
    }

    [Fact]
    public void AutoSyncSeesALayoutChangedInAnotherProfile()
    {
        var single = Profile("Single", (Commander, "single cmd"), (Major, "single major"));
        var split = Profile("Split", (Commander, "split cmd"), (Major, "split major"));
        var old = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        foreach (var file in Directory.EnumerateFiles(_root, "*.dat", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(file, old);

        var changed = old.AddHours(3);
        File.SetLastWriteTimeUtc(CharacterSettingsSync.CharacterFilePath(split, Commander), changed);

        var plan = SettingsSyncPlanner.BuildFromDisk(Config(single, SettingsSyncMode.WithinEach, split));

        Assert.Equal(changed, SettingsSyncPlanner.NewestWrite(plan, Commander));
    }

    [Fact]
    public void RestoringAWithinEachSyncPutsEveryProfileBack()
    {
        var single = Profile("Single", (Commander, "single cmd"), (Major, "single major"));
        var split = Profile("Split", (Commander, "split cmd"), (Major, "split major"));
        var sync = new CharacterSettingsSync(_backups);

        SettingsSyncPlanner.Run(SettingsSyncPlanner.BuildFromDisk(Config(single, SettingsSyncMode.WithinEach, split)),
            Commander, sync, Guid.NewGuid());
        foreach (var backup in sync.LatestBatch(new[] { single, split }))
            sync.Restore(backup);

        Assert.Equal("single major", Read(single, Major));
        Assert.Equal("split major", Read(split, Major));
    }

    [Fact]
    public void ModeIsSavedByNameAndDefaultsToCopyInto()
    {
        var path = Path.Combine(_root, "multibox.json");
        Directory.CreateDirectory(_root);
        var config = new MultiBoxConfig();
        Assert.Equal(SettingsSyncMode.CopyInto, config.SettingsSync.Mode);

        config.SettingsSync.Mode = SettingsSyncMode.WithinEach;
        config.Save(path);

        Assert.Contains("\"WithinEach\"", File.ReadAllText(path));
        Assert.Equal(SettingsSyncMode.WithinEach, MultiBoxConfig.Load(path).SettingsSync.Mode);
    }

    [Fact]
    public void AConfigWrittenBeforeModesExistedStillLoads()
    {
        var path = Path.Combine(_root, "old.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "{ \"SettingsSync\": { \"SourceCharacterId\": 1899648001, \"AutoSync\": true } }");

        var loaded = MultiBoxConfig.Load(path).SettingsSync;

        Assert.Equal(SettingsSyncMode.CopyInto, loaded.Mode);
        Assert.True(loaded.AutoSync);
    }
}

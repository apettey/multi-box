using MultiBox.Core.Config;
using MultiBox.Core.Settings;
using Xunit;

namespace MultiBox.Core.Tests;

/// <summary>
/// The settings files are opaque binary that MultiBox never parses, so these tests use
/// stand-in bytes in a temporary EVE-shaped folder. The ids are the real pilots from samples/.
/// </summary>
public sealed class SettingsSyncTests : IDisposable
{
    private const long Commander = 1899648001;
    private const long Major = 1532110739;
    private const long Lieutenant = 1462945193;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"multibox_settings_{Guid.NewGuid():N}");
    private readonly string _profile;
    private readonly string _backups;

    public SettingsSyncTests()
    {
        _profile = Path.Combine(_root, "CCP", "EVE", "c_eve_sharedcache_tq_tranquility", "settings_Default");
        _backups = Path.Combine(_root, "backups");
        Directory.CreateDirectory(_profile);

        Write($"core_char_{Commander}.dat", "commander layout");
        Write($"core_char_{Major}.dat", "major layout");
        Write($"core_char_{Lieutenant}.dat", "lieutenant layout");
        Write("core_char__.dat", "shared default");
        Write("core_user_20000001.dat", "account settings");
        Write("prefs.ini", "display settings");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(_profile, name), content);
    private string Read(string name) => File.ReadAllText(Path.Combine(_profile, name));
    private static string Char(long id) => $"core_char_{id}.dat";

    [Fact]
    public void FindsTranquilityProfilesAndSkipsTestServers()
    {
        Directory.CreateDirectory(Path.Combine(_root, "CCP", "EVE", "c_eve_sharedcache_sisi_singularity", "settings_Default"));
        Directory.CreateDirectory(Path.Combine(_root, "CCP", "EVE", "c_eve_sharedcache_tq_tranquility", "settings_Mining"));

        var profiles = EveSettingsLocator.FindProfiles(Path.Combine(_root, "CCP", "EVE"));

        Assert.Equal(new[] { "Default", "Mining" }, profiles.Select(p => p.Name));
        Assert.All(profiles, p => Assert.Contains("tranquility", p.ServerFolder));
    }

    [Fact]
    public void ListsOnlyPerCharacterFiles()
    {
        var files = EveSettingsLocator.CharacterFiles(_profile);

        Assert.Equal(new[] { Lieutenant, Major, Commander }, files.Select(f => f.CharacterId));
    }

    [Theory]
    [InlineData("core_char_1899648001.dat", 1899648001L)]
    [InlineData("core_char__.dat", null)]
    [InlineData("core_user_1899648001.dat", null)]
    [InlineData("core_char_1899648001.dat.bak", null)]
    [InlineData("core_char_abc.dat", null)]
    public void ParsesCharacterIdOnlyFromCharacterFiles(string name, long? expected) =>
        Assert.Equal(expected, EveSettingsLocator.TryParseCharacterId(name));

    [Fact]
    public void CopiesTheSourceOverEveryTarget()
    {
        var sync = new CharacterSettingsSync(_backups);

        var result = sync.Apply(_profile, Commander, new[] { Major, Lieutenant });

        Assert.Equal(new[] { Major, Lieutenant }, result.Copied);
        Assert.Equal("commander layout", Read(Char(Major)));
        Assert.Equal("commander layout", Read(Char(Lieutenant)));
        Assert.Equal("commander layout", Read(Char(Commander)));
    }

    [Fact]
    public void NeverTouchesAccountSharedOrDisplayFiles()
    {
        new CharacterSettingsSync(_backups).Apply(_profile, Commander, new[] { Major, Lieutenant });

        Assert.Equal("account settings", Read("core_user_20000001.dat"));
        Assert.Equal("shared default", Read("core_char__.dat"));
        Assert.Equal("display settings", Read("prefs.ini"));
        Assert.Empty(Directory.GetFiles(_profile, "*.multibox-tmp"));
    }

    [Fact]
    public void DoesNotCreateFilesForCharactersThatNeverLoggedIn()
    {
        const long neverLoggedIn = 2112000001;

        var result = new CharacterSettingsSync(_backups).Apply(_profile, Commander, new[] { neverLoggedIn });

        Assert.Empty(result.Copied);
        Assert.Null(result.BackupPath);
        Assert.False(File.Exists(Path.Combine(_profile, Char(neverLoggedIn))));
    }

    [Fact]
    public void RefusesASourceWithNoFile()
    {
        var sync = new CharacterSettingsSync(_backups);

        Assert.Throws<FileNotFoundException>(() => sync.Apply(_profile, 2112000001, new[] { Major }));
        Assert.Equal("major layout", Read(Char(Major)));
    }

    [Fact]
    public void BacksUpBeforeOverwritingAndRestoresExactly()
    {
        var sync = new CharacterSettingsSync(_backups);

        var result = sync.Apply(_profile, Commander, new[] { Major, Lieutenant });

        Assert.NotNull(result.BackupPath);
        var backup = Assert.Single(sync.ListBackups());
        Assert.Equal(_profile, backup.ProfilePath);
        Assert.Equal(3, backup.FileCount);

        var restored = sync.Restore(backup);

        Assert.Equal(3, restored);
        Assert.Equal("major layout", Read(Char(Major)));
        Assert.Equal("lieutenant layout", Read(Char(Lieutenant)));
        Assert.Equal("commander layout", Read(Char(Commander)));
    }

    [Fact]
    public void KeepsOnlyTheNewestBackups()
    {
        var sync = new CharacterSettingsSync(_backups, keepBackups: 2);

        for (var i = 0; i < 4; i++)
        {
            Write(Char(Major), $"edit {i}");
            sync.Apply(_profile, Commander, new[] { Major });
            Thread.Sleep(5);
        }

        Assert.Equal(2, sync.ListBackups().Count);
    }

    private string MakeProfile(string name, params (long Id, string Content)[] files)
    {
        var path = Path.Combine(_root, "CCP", "EVE", "c_eve_sharedcache_tq_tranquility", "settings_" + name);
        Directory.CreateDirectory(path);
        foreach (var (id, content) in files)
            File.WriteAllText(Path.Combine(path, Char(id)), content);
        return path;
    }

    [Fact]
    public void ReportsEachProfilesCharacterCountAndNewestWrite()
    {
        var split = MakeProfile("Split_Screen", (Major, "split major"));
        var newest = new DateTime(2026, 10, 17, 21, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(split, Char(Major)), newest);
        MakeProfile("NewProfile");

        var profiles = EveSettingsLocator.FindProfiles(Path.Combine(_root, "CCP", "EVE"))
            .ToDictionary(p => p.Name);

        Assert.Equal(3, profiles["Default"].CharacterCount);
        Assert.Equal(newest, profiles["Split_Screen"].LastWriteUtc);
        Assert.Equal(0, profiles["NewProfile"].CharacterCount);
        Assert.Null(profiles["NewProfile"].LastWriteUtc);
    }

    [Fact]
    public void AppliesTheSourceLayoutIntoAnotherProfileIncludingTheSourcesOwnCopy()
    {
        var split = MakeProfile("Split_Screen", (Commander, "old commander"), (Major, "old major"));

        var result = new CharacterSettingsSync(_backups)
            .Apply(_profile, Commander, split, new[] { Commander, Major, Lieutenant });

        // Lieutenant has never logged in under Split_Screen, so it gets no file there.
        Assert.Equal(new[] { Commander, Major }, result.Copied);
        Assert.Equal("commander layout", File.ReadAllText(Path.Combine(split, Char(Commander))));
        Assert.Equal("commander layout", File.ReadAllText(Path.Combine(split, Char(Major))));
        Assert.False(File.Exists(Path.Combine(split, Char(Lieutenant))));
        Assert.Equal("major layout", Read(Char(Major)));
    }

    [Fact]
    public void OtherProfileTargetsKeepExclusionsButIncludeTheSource()
    {
        var config = new SettingsSyncConfig
        {
            ProfilePath = _profile,
            SourceCharacterId = Commander,
            ExcludedCharacterIds = { Lieutenant },
            AlsoApplyToProfilePaths = { _profile, @"C:\b", @"c:\B", "" }
        };

        Assert.Equal(new[] { Commander, Major }, config.TargetsInOtherProfile(new[] { Commander, Major, Lieutenant }));
        Assert.Equal(new[] { @"C:\b" }, config.OtherProfiles());
    }

    [Fact]
    public void RestoresEveryProfileASyncTouched()
    {
        var split = MakeProfile("Split_Screen", (Major, "split major"));
        var sync = new CharacterSettingsSync(_backups);
        var batch = Guid.NewGuid();

        sync.Apply(_profile, Commander, new[] { Major }, batch);
        sync.Apply(_profile, Commander, split, new[] { Major }, batch);

        var backups = sync.LatestBatch(new[] { split });
        Assert.Equal(2, backups.Count);
        foreach (var backup in backups)
            sync.Restore(backup);

        Assert.Equal("major layout", Read(Char(Major)));
        Assert.Equal("split major", File.ReadAllText(Path.Combine(split, Char(Major))));
    }

    [Fact]
    public void KeepsBackupsBySyncRatherThanByZip()
    {
        var split = MakeProfile("Split_Screen", (Major, "split major"));
        var sync = new CharacterSettingsSync(_backups, keepBackups: 1);

        for (var i = 0; i < 3; i++)
        {
            var batch = Guid.NewGuid();
            sync.Apply(_profile, Commander, new[] { Major }, batch);
            sync.Apply(_profile, Commander, split, new[] { Major }, batch);
            Thread.Sleep(5);
        }

        var left = sync.ListBackups();
        Assert.Equal(2, left.Count);
        Assert.Single(left.Select(b => b.BatchId).Distinct());
    }

    [Fact]
    public void ExclusionsAndTheSourceAreNeverTargets()
    {
        var config = new SettingsSyncConfig { SourceCharacterId = Commander, ExcludedCharacterIds = { Lieutenant } };

        Assert.Equal(new[] { Major }, config.TargetsAmong(new[] { Commander, Major, Lieutenant }));
    }

    [Fact]
    public void RoundTripsThroughConfigJson()
    {
        var path = Path.Combine(_root, "multibox.json");
        var config = new MultiBoxConfig();
        config.SettingsSync.ProfilePath = _profile;
        config.SettingsSync.SourceCharacterId = Commander;
        config.SettingsSync.ExcludedCharacterIds.Add(Lieutenant);
        config.SettingsSync.AutoSync = true;
        config.Save(path);

        var loaded = MultiBoxConfig.Load(path).SettingsSync;

        Assert.Equal(_profile, loaded.ProfilePath);
        Assert.Equal(Commander, loaded.SourceCharacterId);
        Assert.Equal(new[] { Lieutenant }, loaded.ExcludedCharacterIds);
        Assert.True(loaded.AutoSync);
    }
}

public class AutoSyncGateTests
{
    private static readonly DateTime T0 = new(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    [Fact]
    public void WaitsForTheFleetToBeDownForTheSettlePeriod()
    {
        var gate = new AutoSyncGate(Settle);
        var written = T0.AddMinutes(-5);

        Assert.False(gate.ShouldSync(T0, openClients: 3, written, lastSyncUtc: null));
        Assert.False(gate.ShouldSync(T0.AddSeconds(1), openClients: 0, written, null));
        Assert.False(gate.ShouldSync(T0.AddSeconds(10), openClients: 0, written, null));
        Assert.True(gate.ShouldSync(T0.AddSeconds(11), openClients: 0, written, null));
    }

    [Fact]
    public void WaitsForAClosingClientToFinishWritingItsFiles()
    {
        var gate = new AutoSyncGate(Settle);
        gate.ShouldSync(T0, 0, T0.AddMinutes(-5), null);

        // The last client flushed its layout 15 s in, after its window had already gone.
        var flushed = T0.AddSeconds(15);
        Assert.False(gate.ShouldSync(T0.AddSeconds(20), 0, flushed, null));
        Assert.True(gate.ShouldSync(T0.AddSeconds(26), 0, flushed, null));
    }

    [Fact]
    public void AClientOpeningResetsTheWait()
    {
        var gate = new AutoSyncGate(Settle);
        var written = T0.AddMinutes(-5);

        gate.ShouldSync(T0, 0, written, null);
        gate.ShouldSync(T0.AddSeconds(8), 1, written, null);

        Assert.False(gate.ShouldSync(T0.AddSeconds(12), 0, written, null));
        Assert.True(gate.ShouldSync(T0.AddSeconds(23), 0, written, null));
    }

    [Fact]
    public void DoesNotRepeatWhenNothingChangedSinceTheLastSync()
    {
        var gate = new AutoSyncGate(Settle);
        var written = T0.AddMinutes(-5);
        var lastSync = T0.AddMinutes(-1);

        gate.ShouldSync(T0, 0, written, lastSync);

        Assert.False(gate.ShouldSync(T0.AddMinutes(1), 0, written, lastSync));
        Assert.True(gate.ShouldSync(T0.AddMinutes(1), 0, lastSync.AddSeconds(5), lastSync));
    }
}

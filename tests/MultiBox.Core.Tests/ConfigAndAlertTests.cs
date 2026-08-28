using MultiBox.Core.Config;
using MultiBox.Core.Model;
using MultiBox.Core.Parsing;
using MultiBox.Core.Stats;
using Xunit;

namespace MultiBox.Core.Tests;

public class ConfigTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var path = Path.Combine(Path.GetTempPath(), $"multibox_{Guid.NewGuid():N}.json");
        try
        {
            var config = new MultiBoxConfig();
            config.SetPanelLocation("MainWindow", null, new Pt(120, 340));
            config.ClientLayout[MultiBoxConfig.ClientKey("Commander Tyrael")] =
                new ClientLayout(10, 20, 1920, 1080);
            config.CharacterIds["Commander Tyrael"] = 1899648001;
            config.Save(path);

            var loaded = MultiBoxConfig.Load(path);

            Assert.Equal(new Pt(120, 340), loaded.GetPanelLocation("MainWindow", null, Pt.Zero));
            Assert.Equal(1920, loaded.ClientLayout["EVE - Commander Tyrael"].Width);
            Assert.Equal(1899648001, loaded.CharacterIds["Commander Tyrael"]);
            Assert.True(loaded.Alerts[nameof(EwarType.Jam)].Enabled);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void PerClientLayoutOverridesFlatLayoutOnlyWhenEnabled()
    {
        var config = new MultiBoxConfig();
        config.SetPanelLocation("MainWindow", null, new Pt(10, 10));

        config.EnablePerClientLayouts = true;
        config.SetPanelLocation("MainWindow", "EVE - Major Tyrael", new Pt(900, 500));

        Assert.Equal(new Pt(900, 500), config.GetPanelLocation("MainWindow", "EVE - Major Tyrael", Pt.Zero));
        // Falls back to the flat layout for a client with no specific arrangement.
        Assert.Equal(new Pt(10, 10), config.GetPanelLocation("MainWindow", "EVE - Other", Pt.Zero));

        config.EnablePerClientLayouts = false;
        Assert.Equal(new Pt(10, 10), config.GetPanelLocation("MainWindow", "EVE - Major Tyrael", Pt.Zero));
    }

    [Fact]
    public void ImportsAnEveOPreviewLayoutInEitherPointFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eveo_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
            {
              "ConfigVersion": 1,
              "ClientLayout": {
                "EVE - Commander Tyrael": { "X": 100, "Y": 50, "Width": 1600, "Height": 900, "IsMaximized": false }
              },
              "FlatLayout": {
                "EVE - Commander Tyrael": { "X": 5, "Y": 15 },
                "EVE - Major Tyrael": "300,400"
              }
            }
            """);

            var config = new MultiBoxConfig();
            Assert.True(EveOPreviewImport.TryImport(path, config));

            Assert.Equal(1600, config.ClientLayout["EVE - Commander Tyrael"].Width);
            Assert.Equal(new Pt(5, 15), config.FlatLayout["EVE - Commander Tyrael"]);
            Assert.Equal(new Pt(300, 400), config.FlatLayout["EVE - Major Tyrael"]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void EveryLoggableEwarTypeHasAnAlertConfigured()
    {
        var config = new MultiBoxConfig();
        foreach (var type in Enum.GetValues<EwarType>().Where(EwarTypeInfo.IsLogged))
            Assert.True(config.Alerts.ContainsKey(type.ToString()), $"no alert configured for {type}");
    }

    [Fact]
    public void EachAlertUsesADistinctSoundSoTypesCanBeToldApart()
    {
        var config = new MultiBoxConfig();
        var signatures = config.Alerts
            .Where(kv => kv.Value.Enabled)
            .Select(kv => $"{kv.Value.Tone}:{kv.Value.Frequency}")
            .ToList();

        Assert.Equal(signatures.Count, signatures.Distinct().Count());
    }
}

public class EwarStateTrackerTests
{
    [Fact]
    public void FiresOnceOnApplicationAndAgainOnlyAfterItLapses()
    {
        var tracker = new EwarStateTracker(TimeSpan.FromSeconds(10));
        var applied = new List<EwarType>();
        var cleared = new List<EwarType>();
        tracker.EwarApplied += a => applied.Add(a.Type);
        tracker.EwarCleared += t => cleared.Add(t);

        var t0 = new DateTime(2026, 8, 28, 17, 0, 0, DateTimeKind.Utc);

        Assert.True(tracker.Apply(EwarType.WarpScramble, t0, "Anchoring Damavik", null));
        // A cycling scrambler re-logs constantly; that must not re-alert.
        Assert.False(tracker.Apply(EwarType.WarpScramble, t0.AddSeconds(3), "Anchoring Damavik", null));
        Assert.False(tracker.Apply(EwarType.WarpScramble, t0.AddSeconds(6), "Anchoring Damavik", null));
        Assert.Single(applied);

        Assert.True(tracker.IsActive(EwarType.WarpScramble, t0.AddSeconds(10)));

        // Nothing refreshed it for longer than the hold, so it lapses.
        tracker.Expire(t0.AddSeconds(30));
        Assert.Single(cleared);
        Assert.False(tracker.IsActive(EwarType.WarpScramble, t0.AddSeconds(30)));

        Assert.True(tracker.Apply(EwarType.WarpScramble, t0.AddSeconds(31), "Anchoring Damavik", null));
        Assert.Equal(2, applied.Count);
    }

    [Fact]
    public void TracksEffectTypesIndependently()
    {
        var tracker = new EwarStateTracker(TimeSpan.FromSeconds(10));
        var t0 = DateTime.UtcNow;

        tracker.Apply(EwarType.Jam, t0, "Rook", "Radar ECM II");
        tracker.Apply(EwarType.WarpScramble, t0, "Damavik", null);

        Assert.True(tracker.IsActive(EwarType.Jam, t0));
        Assert.True(tracker.IsActive(EwarType.WarpScramble, t0));
        Assert.False(tracker.IsActive(EwarType.Web, t0));
    }

    [Fact]
    public void AnEwarEventAgainstAFleetMateDoesNotLightUpThisPilot()
    {
        var monitor = new CharacterMonitor("Major Tyrael", 1532110739);
        var parser = new GamelogParser("Major Tyrael");

        // Major's log recording a scramble applied to Commander, not to Major.
        var e = parser.ParseLine(
            "[ 2026.08.28 16:23:52 ] (combat) <b>Warp scramble attempt</b> <font size=10>from</font> " +
            "<b>Anchoring Damavik</b> <font size=10>to <b></font><font size=12><b>Retribution</b> /</font>" +
            "<font size=12>/ <b>Commander Tyrael</b></font> <font size=10><b>VI.TA</b> /</font>");

        Assert.NotNull(e);
        Assert.Equal("Commander Tyrael", e!.Victim);

        monitor.Apply(e);
        Assert.False(monitor.Ewar.IsActive(EwarType.WarpScramble, e.Timestamp));
    }

    [Fact]
    public void AnEwarEventAgainstThisPilotDoesLightUp()
    {
        var monitor = new CharacterMonitor("Commander Tyrael", 1899648001);
        var parser = new GamelogParser("Commander Tyrael");

        var e = parser.ParseLine(
            "[ 2026.08.28 16:23:52 ] (combat) <b>Warp scramble attempt</b> <font size=10>from</font> " +
            "<b>Anchoring Damavik</b> <font size=10>to <b></font>you!");

        Assert.NotNull(e);
        monitor.Apply(e!);
        Assert.True(monitor.Ewar.IsActive(EwarType.WarpScramble, e!.Timestamp));
    }
}

public class RollingWindowTests
{
    [Fact]
    public void AveragesOverTheWindowAndDiscardsOlderSamples()
    {
        var window = new RollingWindow(TimeSpan.FromSeconds(10));
        var t0 = new DateTime(2026, 8, 28, 17, 0, 0, DateTimeKind.Utc);

        window.Add(t0, 500);
        window.Add(t0.AddSeconds(1), 500);
        Assert.Equal(100, window.PerSecond(t0.AddSeconds(1)), 1);

        // Both samples are now outside the ten second window.
        Assert.Equal(0, window.PerSecond(t0.AddSeconds(30)), 1);
    }
}

public class LogFileNameTests
{
    [Theory]
    [InlineData("20260828_160148_1899648001.txt", 1899648001L, null)]
    [InlineData("Fleet_20260828_162059_1899648001.txt", 1899648001L, "Fleet")]
    [InlineData("ViTA Intel_20260828_160148_1899648001.txt", 1899648001L, "ViTA Intel")]
    public void ExtractsCharacterIdAndChannel(string fileName, long expectedId, string? expectedChannel)
    {
        var parsed = LogFileName.TryParse(Path.Combine("C:", "logs", fileName));

        Assert.NotNull(parsed);
        Assert.Equal(expectedId, parsed!.CharacterId);
        Assert.Equal(expectedChannel, parsed.Channel);
        Assert.Equal(expectedChannel is not null, parsed.IsChatLog);
    }

    [Fact]
    public void RejectsUnrelatedFiles()
    {
        Assert.Null(LogFileName.TryParse("notes.txt"));
        Assert.Null(LogFileName.TryParse("20260828_160148.txt"));
    }
}

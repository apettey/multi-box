using MultiBox.Core.Model;
using MultiBox.Core.Parsing;
using MultiBox.Core.Stats;
using Xunit;

namespace MultiBox.Core.Tests;

/// <summary>
/// The session hands every parsed event to every character's monitor, so each monitor is
/// responsible for deciding whether an event is its own. Getting this wrong silently sums
/// the whole fleet's numbers onto each tile, which looks plausible and is completely wrong.
/// </summary>
public class AttributionTests
{
    private static GameLogEvent Parse(string listener, string line)
    {
        var e = new GamelogParser(listener).ParseLine(line);
        Assert.NotNull(e);
        return e!;
    }

    [Fact]
    public void OutgoingDamageIsCreditedOnlyToThePilotWhoseLogRecordedIt()
    {
        var commander = new CharacterMonitor("Commander Tyrael", 1899648001);
        var major = new CharacterMonitor("Major Tyrael", 1532110739);

        // A line from Commander's log: Commander shooting an NPC.
        var e = Parse("Commander Tyrael",
            "[ 2026.08.28 16:24:15 ] (combat) <color=0xff00ffff><b>295</b> <color=0x77ffffff><font size=10>to</font> " +
            "<b><color=0xffffffff>Sparkneedle Tessella</b><font size=10><color=0x77ffffff> - Small Focused Pulse Laser II - Penetrates");

        commander.Apply(e);
        major.Apply(e);

        Assert.True(commander.DamageOut.TotalInWindow(e.Timestamp) > 0);
        Assert.Equal(0, major.DamageOut.TotalInWindow(e.Timestamp));
    }

    [Fact]
    public void OutgoingRepairIsCreditedOnlyToTheRepairer()
    {
        var major = new CharacterMonitor("Major Tyrael", 1532110739);
        var lieutent = new CharacterMonitor("Lieutent Tyrael", 1462945193);

        // From Major's log: Major repairing Commander.
        var e = Parse("Major Tyrael",
            "[ 2026.08.28 16:24:02 ] (combat) <b>103</b><font size=10> remote armor repaired to </font>" +
            "<font size=12><b>Retribution</b> /</font><font size=12>/ <b>Commander Tyrael</b></font> " +
            "<font size=10><b>VI.TA</b> /</font><font size=10> - Coreli A-Type Small Remote Armor Repairer</font>");

        major.Apply(e);
        lieutent.Apply(e);

        Assert.Equal(103, major.RepsOut.TotalInWindow(e.Timestamp));
        Assert.Equal(0, lieutent.RepsOut.TotalInWindow(e.Timestamp));
    }

    [Fact]
    public void IncomingDamageIsCreditedToTheVictimEvenWhenAnotherClientLoggedIt()
    {
        var commander = new CharacterMonitor("Commander Tyrael", 1899648001);
        var major = new CharacterMonitor("Major Tyrael", 1532110739);

        // From Commander's own log, so the victim resolves from "you".
        var e = Parse("Commander Tyrael",
            "[ 2026.08.28 16:23:51 ] (combat) <b>36</b> <font size=10>from</font> " +
            "<b>Anchoring Damavik</b><font size=10> - Hits");

        commander.Apply(e);
        major.Apply(e);

        Assert.Equal(36, commander.DamageIn.TotalInWindow(e.Timestamp));
        Assert.Equal(0, major.DamageIn.TotalInWindow(e.Timestamp));
    }

    [Fact]
    public void FourMonitorsFedTheSameEventStreamKeepSeparateNumbers()
    {
        var monitors = new[]
        {
            new CharacterMonitor("Commander Tyrael", 1),
            new CharacterMonitor("Major Tyrael", 2),
            new CharacterMonitor("Lieutent Tyrael", 3)
        };

        var events = new[]
        {
            Parse("Commander Tyrael", "[ 2026.08.28 16:24:15 ] (combat) <b>100</b> <font size=10>to</font> <b>Rat</b><font size=10> - Hits"),
            Parse("Major Tyrael", "[ 2026.08.28 16:24:15 ] (combat) <b>50</b> <font size=10>to</font> <b>Rat</b><font size=10> - Hits")
        };

        foreach (var e in events)
        foreach (var m in monitors)
            m.Apply(e);

        var at = events[0].Timestamp;
        Assert.Equal(100, monitors[0].DamageOut.TotalInWindow(at));
        Assert.Equal(50, monitors[1].DamageOut.TotalInWindow(at));
        Assert.Equal(0, monitors[2].DamageOut.TotalInWindow(at));
    }

    [Fact]
    public void LearnsAPilotsShipFromAnotherClientsWitnessLine()
    {
        var commander = new CharacterMonitor("Commander Tyrael", 1899648001);

        // Major's log naming Commander's ship while recording a scramble against him.
        var e = Parse("Major Tyrael",
            "[ 2026.08.28 16:23:52 ] (combat) <b>Warp scramble attempt</b> <font size=10>from</font> " +
            "<b>Anchoring Damavik</b> <font size=10>to <b></font><font size=12><b>Retribution</b> /</font>" +
            "<font size=12>/ <b>Commander Tyrael</b></font> <font size=10><b>VI.TA</b> /</font>");

        commander.Apply(e);

        Assert.Equal("Retribution", commander.Ship);
    }
}

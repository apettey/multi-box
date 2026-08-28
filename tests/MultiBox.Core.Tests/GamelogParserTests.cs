using MultiBox.Core.Model;
using MultiBox.Core.Parsing;
using Xunit;

namespace MultiBox.Core.Tests;

public class GamelogParserTests
{
    private readonly GamelogParser _commander = new("Commander Tyrael");

    [Fact]
    public void ParsesIncomingWarpScrambleAgainstSelf()
    {
        const string line = "[ 2026.08.28 16:23:52 ] (combat) <color=0xffffffff><b>Warp scramble attempt</b> " +
                            "<color=0x77ffffff><font size=10>from</font> <color=0xffffffff><b>Anchoring Damavik</b> " +
                            "<color=0x77ffffff><font size=10>to <b><color=0xffffffff></font>you!";

        var e = _commander.ParseLine(line);

        Assert.NotNull(e);
        Assert.Equal(CombatEventKind.Ewar, e!.Kind);
        Assert.Equal(EwarType.WarpScramble, e.Ewar);
        Assert.Equal(Direction.Incoming, e.Direction);
        Assert.Equal("Anchoring Damavik", e.Counterparty!.Name);
        Assert.Equal("Commander Tyrael", e.Victim);
    }

    [Fact]
    public void ParsesScrambleWitnessedAgainstAnotherPilot()
    {
        // The same event as above, as recorded in a different client's log.
        const string line = "[ 2026.08.28 16:23:52 ] (combat) <color=0xffffffff><b>Warp scramble attempt</b> " +
                            "<color=0x77ffffff><font size=10>from</font> <color=0xffffffff><b>Anchoring Damavik</b> " +
                            "<color=0x77ffffff><font size=10>to <b><color=0xffffffff></font><font size=12>" +
                            "<color=0xFFFF5900><b>Retribution</b> /</color></font><font size=12><color=0xFFFFB300>" +
                            "/ <b>Commander Tyrael</b></color></font> <font size=10><color=0xFF7FFF1F><b>VI.TA</b> /</color></font>";

        var major = new GamelogParser("Major Tyrael");
        var e = major.ParseLine(line);

        Assert.NotNull(e);
        Assert.Equal(EwarType.WarpScramble, e!.Ewar);
        Assert.Equal("Commander Tyrael", e.Victim);
        Assert.Equal("Retribution", EveEntity.Parse("Retribution // Commander Tyrael VI.TA /").Ship);
    }

    [Fact]
    public void TheSameScrambleFromTwoClientsSharesADedupKey()
    {
        const string asVictim = "[ 2026.08.28 16:23:52 ] (combat) <b>Warp scramble attempt</b> <font size=10>from</font> " +
                                "<b>Anchoring Damavik</b> <font size=10>to <b></font>you!";
        const string asWitness = "[ 2026.08.28 16:23:52 ] (combat) <b>Warp scramble attempt</b> <font size=10>from</font> " +
                                 "<b>Anchoring Damavik</b> <font size=10>to <b></font><font size=12><b>Retribution</b> /</font>" +
                                 "<font size=12>/ <b>Commander Tyrael</b></font> <font size=10><b>VI.TA</b> /</font>";

        var victimEvent = new GamelogParser("Commander Tyrael").ParseLine(asVictim);
        var witnessEvent = new GamelogParser("Major Tyrael").ParseLine(asWitness);

        Assert.NotNull(victimEvent);
        Assert.NotNull(witnessEvent);
        Assert.Equal(victimEvent!.DedupKey(), witnessEvent!.DedupKey());
    }

    [Fact]
    public void ParsesIncomingEcmJam()
    {
        const string line = "[ 2026.08.28 17:52:29 ] (combat) <color=0xffffffff><b>You're jammed by " +
                            "<font size=12><color=0xFFFF5900><b>Rook</b> /</color></font><font size=12>" +
                            "<color=0xFFFFB300>/ <b>Lieutent Tyrael</b></color></font> " +
                            "<font size=10><color=0xFF7FFF1F><b>VI.TA</b> /</color></font></b>" +
                            "<color=0x77ffffff><font size=10> - Radar ECM II</font>";

        var e = _commander.ParseLine(line);

        Assert.NotNull(e);
        Assert.Equal(EwarType.Jam, e!.Ewar);
        Assert.Equal(Direction.Incoming, e.Direction);
        Assert.Equal("Commander Tyrael", e.Victim);
        Assert.Equal("Lieutent Tyrael", e.Counterparty!.Name);
        Assert.Equal("Radar ECM II", e.Module);
    }

    [Fact]
    public void ParsesOutgoingJam()
    {
        const string line = "[ 2026.08.28 17:52:29 ] (combat) <color=0xffffffff><b><font size=12>" +
                            "<color=0xFFFF5900><b>Garmur</b> /</color></font><font size=12><color=0xFFFFB300>" +
                            "/ <b>Commander Tyrael</b></color></font> <font size=10><color=0xFF7FFF1F>" +
                            "<b>VI.TA</b> /</color></font> jammed</b><color=0x77ffffff><font size=10> - Radar ECM II</font>";

        var lieutent = new GamelogParser("Lieutent Tyrael");
        var e = lieutent.ParseLine(line);

        Assert.NotNull(e);
        Assert.Equal(EwarType.Jam, e!.Ewar);
        Assert.Equal(Direction.Outgoing, e.Direction);
        Assert.Equal("Commander Tyrael", e.Victim);
    }

    [Fact]
    public void ParsesIncomingAndOutgoingDamage()
    {
        var incoming = _commander.ParseLine(
            "[ 2026.08.28 16:23:51 ] (combat) <color=0xffcc0000><b>36</b> <color=0x77ffffff><font size=10>from</font> " +
            "<b><color=0xffffffff>Anchoring Damavik</b><font size=10><color=0x77ffffff> - Hits");

        Assert.NotNull(incoming);
        Assert.Equal(CombatEventKind.Damage, incoming!.Kind);
        Assert.Equal(Direction.Incoming, incoming.Direction);
        Assert.Equal(36, incoming.Amount);
        Assert.Equal("Hits", incoming.Quality);

        var outgoing = _commander.ParseLine(
            "[ 2026.08.28 16:24:15 ] (combat) <color=0xff00ffff><b>295</b> <color=0x77ffffff><font size=10>to</font> " +
            "<b><color=0xffffffff>Sparkneedle Tessella</b><font size=10><color=0x77ffffff> - Small Focused Pulse Laser II - Penetrates");

        Assert.NotNull(outgoing);
        Assert.Equal(Direction.Outgoing, outgoing!.Direction);
        Assert.Equal(295, outgoing.Amount);
        Assert.Equal("Small Focused Pulse Laser II", outgoing.Module);
        Assert.Equal("Penetrates", outgoing.Quality);
    }

    [Fact]
    public void ParsesRemoteRepairDirection()
    {
        const string repairedBy = "[ 2026.08.28 16:24:02 ] (combat) <color=0xffccff66><b>103</b><color=0x77ffffff>" +
                                  "<font size=10> remote armor repaired by </font><b><color=0xffffffff><font size=12>" +
                                  "<color=0xFFFF5900><b>Deacon</b> /</color></font><font size=12><color=0xFFFFB300>" +
                                  "/ <b>Major Tyrael</b></color></font> <font size=10><color=0xFF7FFF1F><b>VI.TA</b> /" +
                                  "</color></font></b><color=0x77ffffff><font size=10> - Coreli A-Type Small Remote Armor Repairer</font>";

        var e = _commander.ParseLine(repairedBy);

        Assert.NotNull(e);
        Assert.Equal(CombatEventKind.RemoteRepair, e!.Kind);
        Assert.Equal(Direction.Incoming, e.Direction);
        Assert.Equal(103, e.Amount);
        Assert.Equal("Major Tyrael", e.Counterparty!.Name);
        Assert.Equal("Commander Tyrael", e.Victim);
    }

    [Fact]
    public void ParsesEnergyNeutralised()
    {
        var e = _commander.ParseLine(
            "[ 2026.08.28 16:24:07 ] (combat) <color=0xffe57f7f><b>66 GJ</b><color=0x77ffffff><font size=10> " +
            "energy neutralized </font><b><color=0xffffffff>Starving Damavik</b><color=0x77ffffff>" +
            "<font size=10> - Starving Damavik</font>");

        Assert.NotNull(e);
        Assert.Equal(CombatEventKind.EnergyNeutralized, e!.Kind);
        Assert.Equal(66, e.Amount);
    }

    [Fact]
    public void IgnoresNonCombatCategories()
    {
        Assert.Null(_commander.ParseLine(
            "[ 2026.08.28 17:55:11 ] (notify) Caracal is too far away to use your Fleeting Compact Stasis Webifier on, " +
            "it needs to be closer than 10000 meters."));
        Assert.Null(_commander.ParseLine("[ 2026.08.28 16:01:49 ] (hint) Attempting to join a channel"));
        Assert.Null(_commander.ParseLine("not a log line at all"));
    }
}

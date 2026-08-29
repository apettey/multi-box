using MultiBox.Core.Model;
using MultiBox.Core.Stats;
using Xunit;

namespace MultiBox.Core.Tests;

/// <summary>
/// Splitting incoming damage by attacker. The headline figure says how much is landing;
/// this says what is landing it, which is the part that decides what to do about it.
/// </summary>
public class ThreatTrackerTests
{
    private static readonly DateTime T0 = new(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

    private static ThreatTracker Tracker() => new(TimeSpan.FromSeconds(10));

    private static EveEntity Npc(string name) => new(name);
    private static EveEntity Player(string pilot, string ship) => new(pilot, ship);

    [Fact]
    public void NothingLandingMeansNoThreats()
    {
        Assert.Empty(Tracker().Top(T0));
        Assert.Null(Tracker().Highest(T0));
    }

    [Fact]
    public void TheHardestHitterComesFirst()
    {
        var t = Tracker();
        t.Add(T0, Npc("Starving Damavik"), 100);
        t.Add(T0, Npc("Blinding Leshak"), 600);
        t.Add(T0, Npc("Lucid Sentinel"), 250);

        var top = t.Top(T0.AddSeconds(1));

        Assert.Equal("Blinding Leshak", top[0].Label);
        Assert.Equal("Lucid Sentinel", top[1].Label);
        Assert.Equal("Starving Damavik", top[2].Label);
    }

    [Fact]
    public void HitsFromOneAttackerAccumulate()
    {
        var t = Tracker();
        t.Add(T0, Npc("Blinding Leshak"), 200);
        t.Add(T0.AddSeconds(1), Npc("Blinding Leshak"), 200);
        t.Add(T0.AddSeconds(2), Npc("Blinding Leshak"), 200);

        var highest = t.Highest(T0.AddSeconds(2));

        Assert.NotNull(highest);
        Assert.Equal(600, highest!.Value.TotalInWindow);
        Assert.Equal(60, highest.Value.Dps);
    }

    [Fact]
    public void APlayerIsLabelledByHullAndNamedByPilot()
    {
        var t = Tracker();
        t.Add(T0, Player("Vint-1", "Ares"), 300);

        var highest = t.Highest(T0)!.Value;

        // The hull is what you can pick out on grid; the pilot is who to report.
        Assert.Equal("Ares", highest.Label);
        Assert.Equal("Vint-1", highest.Pilot);
        Assert.Equal("Ares \"Vint-1\"", highest.Describe());
    }

    [Fact]
    public void AnNpcIsLabelledByItsOwnNameWithNoPilot()
    {
        var t = Tracker();
        t.Add(T0, Npc("Blinding Leshak"), 300);

        var highest = t.Highest(T0)!.Value;

        Assert.Equal("Blinding Leshak", highest.Label);
        Assert.Null(highest.Pilot);
        Assert.Equal("Blinding Leshak", highest.Describe());
    }

    [Fact]
    public void TwoPilotsInTheSameHullStaySeparate()
    {
        var t = Tracker();
        t.Add(T0, Player("Vint-1", "Ares"), 100);
        t.Add(T0, Player("Vint-2", "Ares"), 500);

        var top = t.Top(T0);

        // Keying on the hull would merge these into one 600 damage phantom.
        Assert.Equal(2, top.Count);
        Assert.Equal("Vint-2", top[0].Pilot);
    }

    [Fact]
    public void AttackersFallOutOfTheWindow()
    {
        var t = Tracker();
        t.Add(T0, Npc("Blinding Leshak"), 500);

        // Eleven seconds later, with a ten second window, it is no longer hitting us.
        Assert.Empty(t.Top(T0.AddSeconds(11)));
        Assert.Equal(0, t.Count);
    }

    [Fact]
    public void AStaleAttackerIsForgottenRatherThanKept()
    {
        var t = Tracker();
        for (var i = 0; i < 50; i++)
            t.Add(T0.AddSeconds(i), Npc($"Rat {i}"), 100);

        // A long fight must not accumulate an entry per NPC that ever landed a shot.
        _ = t.Top(T0.AddSeconds(49));
        Assert.InRange(t.Count, 1, 11);
    }

    [Fact]
    public void DamageWithNoNamedSourceIsNotAttributed()
    {
        var t = Tracker();
        t.Add(T0, null, 400);

        // It still counts towards the headline figure; it just cannot be blamed on anyone,
        // and inventing an "unknown" attacker would put it top of the list.
        Assert.Empty(t.Top(T0));
    }

    [Fact]
    public void ZeroDamageDoesNotCreateAThreat()
    {
        var t = Tracker();
        t.Add(T0, Npc("Starving Damavik"), 0);

        Assert.Empty(t.Top(T0));
    }

    [Fact]
    public void TopIsLimitedToWhatWasAskedFor()
    {
        var t = Tracker();
        for (var i = 0; i < 8; i++)
            t.Add(T0, Npc($"Rat {i}"), 100 + i);

        Assert.Equal(3, t.Top(T0, 3).Count);
        Assert.Single(t.Top(T0, 1));
    }

    [Fact]
    public void TiesBreakOnNameSoTheOrderDoesNotFlicker()
    {
        var t = Tracker();
        t.Add(T0, Npc("Zeta"), 100);
        t.Add(T0, Npc("Alpha"), 100);

        // Equal damage must produce a stable order, or the card jitters between them.
        Assert.Equal("Alpha", t.Top(T0)[0].Label);
    }

    [Fact]
    public void TheTrackerFollowsTheStatWindowItWasGiven()
    {
        var wide = new ThreatTracker(TimeSpan.FromSeconds(60));
        wide.Add(T0, Npc("Blinding Leshak"), 600);

        // Still inside a sixty second window where a ten second one would have dropped it.
        Assert.NotNull(wide.Highest(T0.AddSeconds(30)));
        Assert.Equal(10, wide.Highest(T0.AddSeconds(30))!.Value.Dps);
    }
}

using MultiBox.Core.Model;
using MultiBox.Core.Stats;
using Xunit;

namespace MultiBox.Core.Tests;

/// <summary>
/// EVE does not flush its gamelog as it fights. It buffers and writes in bursts, so a line
/// describing something that happened half a minute ago arrives now.
///
/// Measured on a real session: a gamelog file whose last write was 22:03:10 UTC had a newest
/// line stamped 22:02:43 — a 27 second lag, against a 10 second stat window. Reading the
/// windows at wall-clock time therefore threw every sample away the moment it arrived, and
/// the dashboard sat at zero through an entire fight while the combat log, which has no
/// window, filled normally.
/// </summary>
public class FlushLagTests
{
    private const int WindowSeconds = 10;
    private static readonly DateTime Fight = new(2026, 8, 28, 22, 0, 0, DateTimeKind.Utc);

    private static CharacterMonitor Monitor() =>
        new("Lieutent Tyrael", 1, TimeSpan.FromSeconds(WindowSeconds), TimeSpan.FromSeconds(12));

    private static GameLogEvent IncomingDamage(DateTime at, int amount) => new()
    {
        Timestamp = at,
        Kind = CombatEventKind.Damage,
        Direction = Direction.Incoming,
        Listener = "Lieutent Tyrael",
        Victim = "Lieutent Tyrael",
        Amount = amount,
        Counterparty = new EveEntity("Commander Tyrael", "Retribution")
    };

    [Fact]
    public void ReadingAtWallClockLosesEverythingWhenTheLogIsFlushedLate()
    {
        var monitor = Monitor();

        // Three hits, then the client flushes them 27 seconds later.
        var observed = Fight.AddSeconds(27);
        monitor.Apply(IncomingDamage(Fight, 100), observed);
        monitor.Apply(IncomingDamage(Fight.AddSeconds(1), 100), observed);
        monitor.Apply(IncomingDamage(Fight.AddSeconds(2), 100), observed);

        // This is the old behaviour, kept as a test so the reason for the fix stays visible.
        Assert.Equal(0, monitor.DamageIn.PerSecond(observed));
    }

    [Fact]
    public void ReadingOnTheLogsClockKeepsTheSamples()
    {
        var monitor = Monitor();

        var observed = Fight.AddSeconds(27);
        monitor.Apply(IncomingDamage(Fight, 100), observed);
        monitor.Apply(IncomingDamage(Fight.AddSeconds(1), 100), observed);
        monitor.Apply(IncomingDamage(Fight.AddSeconds(2), 100), observed);

        // 300 damage over a 10 second window.
        Assert.Equal(30, monitor.DamageIn.PerSecond(monitor.ProjectedNow(observed)));
    }

    [Fact]
    public void TheProjectedClockAdvancesWithRealTime()
    {
        var monitor = Monitor();
        var observed = Fight.AddSeconds(27);
        monitor.Apply(IncomingDamage(Fight, 100), observed);

        // Five real seconds later the log's clock has also moved five seconds on.
        Assert.Equal(Fight.AddSeconds(5), monitor.ProjectedNow(observed.AddSeconds(5)));
    }

    [Fact]
    public void ReadingsStillDecayToZeroWhenTheShootingStops()
    {
        var monitor = Monitor();
        var observed = Fight.AddSeconds(27);
        monitor.Apply(IncomingDamage(Fight, 100), observed);

        Assert.True(monitor.DamageIn.PerSecond(monitor.ProjectedNow(observed)) > 0);

        // Anchoring to the log must not freeze the numbers on screen after the fight ends.
        var later = observed.AddSeconds(WindowSeconds + 5);
        Assert.Equal(0, monitor.DamageIn.PerSecond(monitor.ProjectedNow(later)));
    }

    [Fact]
    public void BeforeAnyEventTheProjectedClockIsJustWallClock()
    {
        var monitor = Monitor();
        var now = DateTime.UtcNow;

        Assert.Equal(now, monitor.ProjectedNow(now));
    }

    [Fact]
    public void ReplayingACapturedLogIsUnaffected()
    {
        var monitor = Monitor();

        // No observedAt given, as when replaying a file: the log's clock is the only clock,
        // so the projection has to be the identity or replays would stop being reproducible.
        monitor.Apply(IncomingDamage(Fight, 100));

        Assert.Equal(Fight, monitor.ProjectedNow(Fight));
        Assert.Equal(10, monitor.DamageIn.PerSecond(monitor.ProjectedNow(Fight)));
    }

    [Fact]
    public void EwarSurvivesALateFlush()
    {
        var monitor = Monitor();
        var observed = Fight.AddSeconds(27);

        monitor.Apply(new GameLogEvent
        {
            Timestamp = Fight,
            Kind = CombatEventKind.Ewar,
            Direction = Direction.Incoming,
            Listener = "Lieutent Tyrael",
            Victim = "Lieutent Tyrael",
            Ewar = EwarType.WarpScramble,
            Counterparty = new EveEntity("Vint-1", "Ares")
        }, observed);

        // Against wall-clock the 12 second hold has already lapsed before the line arrived.
        Assert.False(monitor.Ewar.IsActive(EwarType.WarpScramble, observed));

        var fresh = Monitor();
        fresh.Apply(new GameLogEvent
        {
            Timestamp = Fight,
            Kind = CombatEventKind.Ewar,
            Direction = Direction.Incoming,
            Listener = "Lieutent Tyrael",
            Victim = "Lieutent Tyrael",
            Ewar = EwarType.WarpScramble,
            Counterparty = new EveEntity("Vint-1", "Ares")
        }, observed);

        Assert.True(fresh.Ewar.IsActive(EwarType.WarpScramble, fresh.ProjectedNow(observed)));
    }
}

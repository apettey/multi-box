using MultiBox.Core.Config;
using Xunit;

namespace MultiBox.Core.Tests;

/// <summary>
/// Cards used to come only from this session's gamelogs, so a client that was running but
/// had written no log — logging switched off, or its last gamelog days old — never appeared
/// on the dashboard even though its window was right there. Observed with Lieutent Tyrael:
/// window "EVE - Lieutent Tyrael" open, newest gamelog two days stale, no card.
/// </summary>
public sealed class RunningClientTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("multibox-running-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MultiBoxSession Session() =>
        new(new MultiBoxConfig { GamelogPath = _dir, ChatlogPath = _dir });

    private void WriteGamelog(string fileName, string listener) =>
        File.WriteAllLines(Path.Combine(_dir, fileName), new[]
        {
            "------------------------------------------------------------",
            "  Gamelog",
            $"  Listener: {listener}",
            "  Session Started: 2026.09.22 19:34:35",
            "------------------------------------------------------------"
        });

    [Fact]
    public void Running_client_with_only_an_old_log_gets_its_real_id()
    {
        WriteGamelog("20260922_193435_1462945193.txt", "Lieutent Tyrael");
        using var session = Session();

        session.Refresh(new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc));
        Assert.Empty(session.Characters);

        session.AddRunningClients(new[] { "Lieutent Tyrael" });

        var monitor = Assert.Single(session.Characters);
        Assert.Equal("Lieutent Tyrael", monitor.Name);
        Assert.Equal(1462945193, monitor.CharacterId);
    }

    [Fact]
    public void Running_client_with_no_log_at_all_gets_a_placeholder()
    {
        using var session = Session();

        session.AddRunningClients(new[] { "Lieutent Tyrael", "Lieutent Tyrael" });

        var monitor = Assert.Single(session.Characters);
        Assert.True(monitor.CharacterId < 0);
        Assert.Equal(MultiBoxSession.PlaceholderId("lieutent tyrael"), monitor.CharacterId);
    }

    [Fact]
    public void Placeholder_gives_way_when_the_real_log_appears()
    {
        using var session = Session();
        session.AddRunningClients(new[] { "Lieutent Tyrael" });

        WriteGamelog("20260924_180735_1462945193.txt", "Lieutent Tyrael");
        session.Refresh();

        var monitor = Assert.Single(session.Characters);
        Assert.Equal(1462945193, monitor.CharacterId);
    }

    [Fact]
    public void Client_already_known_from_logs_is_not_duplicated()
    {
        WriteGamelog("20260924_180735_1532110739.txt", "Major Tyrael");
        using var session = Session();
        session.Refresh();

        session.AddRunningClients(new[] { "Major Tyrael" });

        Assert.Equal(1532110739, Assert.Single(session.Characters).CharacterId);
    }
}

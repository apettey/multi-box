using MultiBox.Core.Config;
using MultiBox.Core.Model;
using Xunit;

namespace MultiBox.Core.Tests;

/// <summary>
/// Logging a character off without closing the client puts the window back to the bare
/// title "EVE". The dashboard only looked for "EVE - &lt;name&gt;", so the client vanished from
/// it until a character logged back in. These pin the title rules that tell the two apart
/// and the ordering that keeps the character-select cards still while clients are focused.
/// </summary>
public sealed class IdleClientTests
{
    [Theory]
    [InlineData("EVE - Lieutent Tyrael", "Lieutent Tyrael")]
    [InlineData("EVE - Major Tyrael ", "Major Tyrael")]
    [InlineData("EVE", null)]
    [InlineData("EVE - ", null)]
    [InlineData("EVE Launcher", null)]
    [InlineData("EVEMon", null)]
    [InlineData("eve - lowercase", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Character_name_comes_only_from_a_logged_in_title(string? title, string? expected) =>
        Assert.Equal(expected, EveWindowTitle.CharacterName(title));

    [Theory]
    [InlineData("EVE", true)]
    [InlineData("EVE - Lieutent Tyrael", false)]
    [InlineData("EVE Launcher", false)]
    [InlineData("EVEMon", false)]
    [InlineData("eve", false)]
    [InlineData(" EVE", false)]
    [InlineData(null, false)]
    public void Character_select_is_the_bare_title(string? title, bool expected) =>
        Assert.Equal(expected, EveWindowTitle.IsCharacterSelect(title));

    [Theory]
    [InlineData("EVE", true)]
    [InlineData("EVE - Lieutent Tyrael", true)]
    [InlineData("EVE - ", true)]
    [InlineData("EVE Launcher", false)]
    [InlineData("EVEMon", false)]
    [InlineData(null, false)]
    public void Any_client_counts_whether_logged_in_or_not(string? title, bool expected) =>
        Assert.Equal(expected, EveWindowTitle.IsClient(title));

    [Fact]
    public void Existing_clients_keep_their_place_when_the_z_order_changes()
    {
        // Alt-tabbing to client 3 brings it to the top of the enumeration.
        var order = FleetLayout.KeepOrder(new[] { 1, 2, 3 }, new[] { 3, 1, 2 });
        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public void A_newly_logged_off_client_joins_at_the_end()
    {
        var order = FleetLayout.KeepOrder(new[] { 1, 2 }, new[] { 9, 2, 1 });
        Assert.Equal(new[] { 1, 2, 9 }, order);
    }

    [Fact]
    public void A_client_that_logged_back_in_or_closed_drops_out()
    {
        var order = FleetLayout.KeepOrder(new[] { 1, 2, 3 }, new[] { 3, 1 });
        Assert.Equal(new[] { 1, 3 }, order);
    }

    [Fact]
    public void Nothing_open_gives_nothing()
    {
        Assert.Empty(FleetLayout.KeepOrder(Array.Empty<int>(), Array.Empty<int>()));
        Assert.Empty(FleetLayout.KeepOrder(new[] { 1, 2 }, Array.Empty<int>()));
    }

    [Fact]
    public void First_scan_takes_the_order_found_and_drops_duplicates()
    {
        var order = FleetLayout.KeepOrder(Array.Empty<int>(), new[] { 5, 4, 5, 6 });
        Assert.Equal(new[] { 5, 4, 6 }, order);
    }

    [Fact]
    public void Many_clients_survive_a_full_reshuffle()
    {
        var current = Enumerable.Range(1, 12).ToList();
        var shuffled = current.AsEnumerable().Reverse().ToList();
        Assert.Equal(current, FleetLayout.KeepOrder(current, shuffled));
    }
}

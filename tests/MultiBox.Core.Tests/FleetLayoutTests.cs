using MultiBox.Core.Config;
using Xunit;

namespace MultiBox.Core.Tests;

public class FleetLayoutTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(10, 5, 2)]
    public void UpToTenCardsFillTwoRows(int cards, int columns, int rows)
    {
        Assert.Equal((columns, rows), FleetLayout.Grid(cards));
    }

    [Theory]
    [InlineData(11, 5, 3)]
    [InlineData(15, 5, 3)]
    [InlineData(20, 5, 4)]
    public void PastTenCardsColumnsStopAtFiveAndRowsGrow(int cards, int columns, int rows)
    {
        // Widening past five would make each card too narrow to read on one 1440p monitor.
        Assert.Equal((columns, rows), FleetLayout.Grid(cards));
    }

    [Fact]
    public void AnEmptyGridIsStillOneCell()
    {
        // The dashboard must not divide by zero on the frame where nothing is running.
        Assert.Equal((1, 1), FleetLayout.Grid(0));
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(4, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(14, 10, 2)]
    [InlineData(14, 4, 4)]
    [InlineData(14, 1, 14)]
    public void SquadCountDividesTheFleet(int characters, int squadSize, int expected)
    {
        Assert.Equal(expected, FleetLayout.SquadCount(characters, squadSize));
    }

    [Fact]
    public void ThereIsAlwaysAtLeastOneSquadTab()
    {
        Assert.Equal(1, FleetLayout.SquadCount(0, 10));
    }

    [Fact]
    public void ASquadSizeOfZeroDoesNotDivideByZero()
    {
        Assert.Equal(5, FleetLayout.SquadCount(5, 0));
    }

    [Fact]
    public void SquadsAreConsecutiveSlicesOfTheOrder()
    {
        var fleet = new[] { "a", "b", "c", "d", "e" };

        Assert.Equal(new[] { "a", "b" }, FleetLayout.Squad(fleet, 0, 2));
        Assert.Equal(new[] { "c", "d" }, FleetLayout.Squad(fleet, 1, 2));
        Assert.Equal(new[] { "e" }, FleetLayout.Squad(fleet, 2, 2));
    }

    [Fact]
    public void ASquadPastTheEndIsEmptyRatherThanAnError()
    {
        Assert.Empty(FleetLayout.Squad(new[] { "a", "b" }, 9, 2));
    }

    [Fact]
    public void ChangingSquadSizeMovesCharactersBetweenSquads()
    {
        var fleet = new[] { "a", "b", "c", "d", "e", "f" };

        // The whole point of the slider: the same order, resliced.
        Assert.Equal(new[] { "a", "b", "c", "d", "e", "f" }, FleetLayout.Squad(fleet, 0, 10));
        Assert.Equal(new[] { "a", "b", "c" }, FleetLayout.Squad(fleet, 0, 3));
        Assert.Equal(new[] { "d", "e", "f" }, FleetLayout.Squad(fleet, 1, 3));
    }
}

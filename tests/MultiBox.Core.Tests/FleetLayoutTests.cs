using MultiBox.Core.Config;
using Xunit;

namespace MultiBox.Core.Tests;

public class FleetLayoutTests
{
    /// <summary>A 1440p monitor with the comms panel alongside — the shape it is tuned for.</summary>
    private const double Wide = FleetLayout.DefaultAreaAspect;

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(6, 3, 2)]
    [InlineData(8, 4, 2)]
    [InlineData(10, 5, 2)]
    public void CommonFleetSizesMatchTheDesignedShapes(int cards, int columns, int rows)
    {
        Assert.Equal((columns, rows), FleetLayout.Grid(cards, Wide));
    }

    [Fact]
    public void TwoCardsSitSideBySideOnAWideArea()
    {
        // The bug this replaced: two cards stacked into one column, leaving a 1440p monitor
        // almost entirely empty.
        Assert.Equal((2, 1), FleetLayout.Grid(2, Wide));
    }

    [Fact]
    public void TwoCardsStackOnATallArea()
    {
        // Same count, portrait monitor. A fixed rule cannot get both of these right.
        Assert.Equal((1, 2), FleetLayout.Grid(2, 1440.0 / 2560.0));
    }

    [Theory]
    // Eleven takes 4x3 rather than 5x3: one empty cell instead of four, for cells barely
    // further from square.
    [InlineData(11, 4, 3)]
    [InlineData(15, 5, 3)]
    [InlineData(20, 5, 4)]
    public void ColumnsStopAtFiveAndRowsGrow(int cards, int columns, int rows)
    {
        // Widening past five would make each card too narrow to read.
        Assert.Equal((columns, rows), FleetLayout.Grid(cards, Wide));
    }

    [Fact]
    public void EveryCardGetsACell()
    {
        for (var n = 1; n <= 20; n++)
        {
            var (columns, rows) = FleetLayout.Grid(n, Wide);
            Assert.True(columns * rows >= n, $"{n} cards do not fit {columns}x{rows}");
            Assert.InRange(columns, 1, FleetLayout.MaxColumns);
        }
    }

    [Fact]
    public void ALayoutIsNeverMoreThanOneRowWasteful()
    {
        // A hole is tolerable; a whole empty row means the shape was chosen badly.
        for (var n = 1; n <= 20; n++)
        {
            var (columns, rows) = FleetLayout.Grid(n, Wide);
            Assert.True(columns * rows - n < columns,
                $"{n} cards laid out {columns}x{rows} wastes an entire row");
        }
    }

    [Fact]
    public void AnEmptyGridIsStillOneCell()
    {
        // The dashboard must not divide by zero on the frame where nothing is running.
        Assert.Equal((1, 1), FleetLayout.Grid(0, Wide));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnusableAspectFallsBackRatherThanThrowing(double aspect)
    {
        // ActualWidth/ActualHeight are zero before the first layout pass.
        Assert.Equal(FleetLayout.Grid(10, Wide), FleetLayout.Grid(10, aspect));
    }

    // --- squads --------------------------------------------------------------------------

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

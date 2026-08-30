using MultiBox.Core.Config;
using Xunit;

namespace MultiBox.Core.Tests;

/// <summary>
/// Choosing the grid by how large a preview it yields, rather than by how tidy the cells are.
/// </summary>
public class GridForPreviewTests
{
    // The card area on a 1440p monitor with the comms panel alongside.
    private const double W = 1960;
    private const double H = 1270;

    [Fact]
    public void ASingleCardTakesTheWholeArea()
    {
        Assert.Equal((1, 1), FleetLayout.GridForPreview(1, W, H));
    }

    [Fact]
    public void TwoCardsGoSideBySide()
    {
        // Stacked, each card is short and the preview is height-limited; side by side each is
        // full height and the preview is limited only by half the width, which is far more.
        Assert.Equal((2, 1), FleetLayout.GridForPreview(2, W, H));
    }

    [Fact]
    public void FourCardsPreferOneTallRowOverTwoByTwo()
    {
        // The tidy-cell rule picks 2x2. A card's fixed rows cost the same whether the card is
        // tall or short, so four full-height cards leave more room for a preview than four
        // half-height ones — even though each is narrower.
        Assert.Equal((4, 1), FleetLayout.GridForPreview(4, W, H));
        Assert.Equal((2, 2), FleetLayout.Grid(4, W / H));
    }

    [Fact]
    public void TenCardsStillTakeFiveByTwo()
    {
        // Five columns is the cap, and two rows of five beats anything shorter.
        Assert.Equal((5, 2), FleetLayout.GridForPreview(10, W, H));
    }

    [Fact]
    public void ColumnsNeverExceedTheCap()
    {
        for (var n = 1; n <= 20; n++)
        {
            var (columns, rows) = FleetLayout.GridForPreview(n, W, H);
            Assert.InRange(columns, 1, FleetLayout.MaxColumns);
            Assert.True(columns * rows >= n, $"{n} cards do not fit {columns}x{rows}");
        }
    }

    [Fact]
    public void AnAreaTooShortForAnyPreviewFallsBackToTidyCells()
    {
        // Every candidate is shorter than the card's fixed rows, so there is no preview to
        // optimise and the tidy-cell rule is the better answer.
        var tiny = FleetLayout.GridForPreview(6, 1960, 200);
        Assert.Equal(FleetLayout.Grid(6, 1960.0 / 200.0), tiny);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(double.NaN, 100)]
    public void AnUnmeasuredAreaFallsBackRatherThanThrowing(double width, double height)
    {
        // ActualWidth and ActualHeight are zero before the first layout pass.
        Assert.Equal(FleetLayout.Grid(6), FleetLayout.GridForPreview(6, width, height));
    }

    [Fact]
    public void NoCardsIsStillOneCell()
    {
        Assert.Equal((1, 1), FleetLayout.GridForPreview(0, W, H));
    }
}

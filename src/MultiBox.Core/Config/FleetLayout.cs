namespace MultiBox.Core.Config;

/// <summary>
/// How many columns and rows a given number of cards is laid out in, and how they divide
/// into squads.
///
/// Kept out of the view model because it is arithmetic with edge cases — one card, an exact
/// multiple of the squad size, more characters than a squad holds — and arithmetic with edge
/// cases belongs somewhere a test can reach it.
/// </summary>
public static class FleetLayout
{
    /// <summary>
    /// Five is the widest the cards stay readable at on a single 1440p monitor, and the shape
    /// the dashboard was designed against.
    /// </summary>
    public const int MaxColumns = 5;

    /// <summary>
    /// Width over height of the card area on a 1440p monitor with the comms panel alongside.
    /// Used when the real measurement is not available yet.
    /// </summary>
    public const double DefaultAreaAspect = 1960.0 / 1270.0;

    /// <summary>
    /// Cells nearer square than this read as squat, further as narrow. Chosen because it is
    /// what a square-ish cell costs: the thumbnail is 16:9 and everything else stacks under
    /// it, so a cell around 1:1 gives the preview real size without starving the numbers.
    /// </summary>
    private const double TargetCellAspect = 1.0;

    /// <summary>
    /// How much an empty cell counts against a layout. High enough that four cards prefer a
    /// tidy 2x2 over a 3x2 with a hole in it, low enough that five cards still take 3x2
    /// rather than stretching into a single row of five.
    /// </summary>
    private const double EmptyCellPenalty = 0.25;

    /// <summary>
    /// Columns and rows for a card count, chosen so the cells come out closest to square for
    /// the space available.
    ///
    /// A fixed rule cannot do this. Two cards on a wide area want to sit side by side; the
    /// same two on a tall one want to stack. The old formula always stacked them, which left
    /// a 1440p monitor almost entirely empty.
    /// </summary>
    /// <param name="areaAspect">Width divided by height of the area the cards occupy.</param>
    public static (int Columns, int Rows) Grid(int cardCount, double areaAspect = DefaultAreaAspect)
    {
        if (cardCount <= 0)
            return (1, 1);

        if (double.IsNaN(areaAspect) || double.IsInfinity(areaAspect) || areaAspect <= 0)
            areaAspect = DefaultAreaAspect;

        var best = (Columns: 1, Rows: cardCount);
        var bestScore = double.MaxValue;

        for (var columns = 1; columns <= Math.Min(cardCount, MaxColumns); columns++)
        {
            var rows = (int)Math.Ceiling(cardCount / (double)columns);

            // cellAspect = (W / columns) / (H / rows), which is areaAspect * rows / columns.
            var cellAspect = areaAspect * rows / columns;

            // Compared in log space so "twice as wide as wanted" and "half as wide" cost the
            // same; a plain difference would quietly favour squat cells over narrow ones.
            var score = Math.Abs(Math.Log(cellAspect / TargetCellAspect))
                        + EmptyCellPenalty * (columns * rows - cardCount);

            if (score >= bestScore)
                continue;

            bestScore = score;
            best = (columns, rows);
        }

        return best;
    }

    /// <summary>
    /// Roughly how much of a card's height is spent on everything that is not the preview:
    /// the name row, the incoming block, the threat line, the four stats, the EWAR row and the
    /// margins between them. Measured from a rendered card rather than derived, because it is
    /// the sum of a dozen paddings.
    /// </summary>
    private const double CardOverhead = 262;

    /// <summary>Share of a card's leftover height the preview takes; the log has the rest.</summary>
    private const double PreviewShare = 0.6;

    private const double PreviewAspect = 16.0 / 9.0;

    /// <summary>
    /// Columns and rows chosen to make the client preview as large as it can be.
    ///
    /// The aspect heuristic optimises for tidy cells, which is a proxy. This optimises for the
    /// thing actually wanted: it works out how big the preview would end up in each candidate
    /// layout and takes the winner. The two disagree because the preview is height-limited —
    /// a card's fixed rows cost the same whether the card is tall or short, so fewer, taller
    /// cells can leave more room for a preview than more, squatter ones.
    /// </summary>
    public static (int Columns, int Rows) GridForPreview(int cardCount, double areaWidth, double areaHeight)
    {
        if (cardCount <= 0)
            return (1, 1);

        if (areaWidth <= 0 || areaHeight <= 0 ||
            double.IsNaN(areaWidth) || double.IsNaN(areaHeight))
            return Grid(cardCount);

        var best = (Columns: 1, Rows: cardCount);
        var bestArea = -1.0;

        for (var columns = 1; columns <= Math.Min(cardCount, MaxColumns); columns++)
        {
            var rows = (int)Math.Ceiling(cardCount / (double)columns);

            var cellWidth = areaWidth / columns;
            var cellHeight = areaHeight / rows;

            var leftover = cellHeight - CardOverhead;
            if (leftover <= 0)
                continue;

            var previewHeight = leftover * PreviewShare;
            var previewWidth = Math.Min(cellWidth, previewHeight * PreviewAspect);

            // Width may bind before height does, in which case the real height is whatever
            // 16:9 allows for that width.
            previewHeight = previewWidth / PreviewAspect;
            var area = previewWidth * previewHeight;

            if (area <= bestArea)
                continue;

            bestArea = area;
            best = (columns, rows);
        }

        // Every candidate was too short to hold a preview at all; fall back to tidy cells.
        return bestArea < 0 ? Grid(cardCount, areaWidth / areaHeight) : best;
    }

    /// <summary>Number of squad tabs needed. Always at least one, so the header is never empty.</summary>
    public static int SquadCount(int characterCount, int squadSize)
    {
        if (characterCount <= 0)
            return 1;
        return (int)Math.Ceiling(characterCount / (double)Math.Max(1, squadSize));
    }

    /// <summary>The slice of characters belonging to one squad tab.</summary>
    public static IEnumerable<T> Squad<T>(IEnumerable<T> characters, int squadIndex, int squadSize)
    {
        var size = Math.Max(1, squadSize);
        return characters.Skip(Math.Max(0, squadIndex) * size).Take(size);
    }
}

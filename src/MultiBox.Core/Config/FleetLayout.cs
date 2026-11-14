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

    /// <summary>
    /// One squad tab's cards when clients at character select are on the grid too: that
    /// squad's slice of the characters, and on the last squad, every idle client after them.
    ///
    /// Idle clients do not count against the squad size. Counting them would push a logged-off
    /// client onto a squad tab of its own whenever the squads were full, which hides it just as
    /// thoroughly as leaving it off the grid.
    /// </summary>
    public static List<T> Squad<T>(IReadOnlyList<T> characters, IReadOnlyList<T> idle, int squadIndex, int squadSize)
    {
        var members = Squad(characters, squadIndex, squadSize).ToList();
        if (squadIndex == SquadCount(characters.Count, squadSize) - 1)
            members.AddRange(idle);
        return members;
    }

    /// <summary>
    /// The F-key label for a card at a position on its squad tab. F1-F12 label the first
    /// twelve; past that there is no function key to name, and a client at character select
    /// has no pilot to switch to, so it gets none.
    /// </summary>
    public static string HotkeyLabel(int position, bool isCharacter) =>
        isCharacter && position is >= 0 and < 12 ? "F" + (position + 1) : string.Empty;

    /// <summary>
    /// The items in <paramref name="found"/>, keeping the order of <paramref name="current"/>
    /// for those already in it and appending new ones in the order found.
    ///
    /// Windows are enumerated in z-order, which changes every time a client is focused. A
    /// list rebuilt straight from the enumeration would reshuffle its cards on each alt-tab.
    /// </summary>
    public static List<T> KeepOrder<T>(IEnumerable<T> current, IEnumerable<T> found)
    {
        var wanted = found.Distinct().ToList();
        var present = wanted.ToHashSet();
        var kept = current.Where(present.Contains).Distinct().ToList();
        var known = kept.ToHashSet();
        kept.AddRange(wanted.Where(f => !known.Contains(f)));
        return kept;
    }
}

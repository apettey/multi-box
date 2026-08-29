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
    /// Columns and rows for a card count. Up to ten cards this fills two rows, widening the
    /// columns as the fleet grows; past ten it keeps five columns and adds rows.
    /// </summary>
    public static (int Columns, int Rows) Grid(int cardCount)
    {
        if (cardCount <= 0)
            return (1, 1);

        var columns = Math.Clamp((int)Math.Ceiling(cardCount / 2.0), 1, MaxColumns);
        var rows = (int)Math.Ceiling(cardCount / (double)columns);
        return (columns, Math.Max(1, rows));
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

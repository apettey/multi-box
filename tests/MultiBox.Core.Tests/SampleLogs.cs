namespace MultiBox.Core.Tests;

/// <summary>
/// Locates the real logs captured from the user's clients in samples/. Tests run against
/// these rather than hand-written fixtures so a parser that only works on invented data
/// cannot pass.
/// </summary>
public static class SampleLogs
{
    public static string Root { get; } = Find();

    public static string GamelogFolder => Path.Combine(Root, "samples", "Gamelogs");

    public static IReadOnlyList<string> Gamelogs =>
        Directory.Exists(GamelogFolder)
            ? Directory.GetFiles(GamelogFolder, "*.txt").OrderBy(f => f).ToList()
            : Array.Empty<string>();

    public static string CommanderLog => Path.Combine(GamelogFolder, "20260828_160148_1899648001.txt");
    public static string MajorLog => Path.Combine(GamelogFolder, "20260828_160151_1532110739.txt");
    public static string LieutentLog => Path.Combine(GamelogFolder, "20260828_160152_1462945193.txt");

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MultiBox.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (MultiBox.sln).");
    }
}

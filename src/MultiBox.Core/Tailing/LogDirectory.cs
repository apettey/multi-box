using MultiBox.Core.Parsing;

namespace MultiBox.Core.Tailing;

/// <summary>
/// Finds EVE's log folders and works out which files belong to the current session.
/// </summary>
public static class LogDirectory
{
    /// <summary>
    /// Candidate locations for EVE's logs, most likely first. OneDrive's "known folder
    /// move" silently relocates Documents, which is where this user's logs actually live,
    /// so the redirected path is probed as well as the classic one.
    /// </summary>
    public static IEnumerable<string> CandidateRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (!string.IsNullOrEmpty(documents))
            yield return Path.Combine(documents, "EVE", "logs");

        if (!string.IsNullOrEmpty(profile))
        {
            yield return Path.Combine(profile, "OneDrive", "Documents", "EVE", "logs");
            yield return Path.Combine(profile, "Documents", "EVE", "logs");
        }

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrive))
            yield return Path.Combine(oneDrive, "Documents", "EVE", "logs");
    }

    /// <summary>First candidate root that exists on this machine, or null.</summary>
    public static string? FindLogRoot() => CandidateRoots().FirstOrDefault(Directory.Exists);

    public static string? FindGamelogFolder(string? configured = null) =>
        Resolve(configured, "Gamelogs");

    public static string? FindChatlogFolder(string? configured = null) =>
        Resolve(configured, "Chatlogs");

    private static string? Resolve(string? configured, string leaf)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        var root = FindLogRoot();
        if (root is null)
            return null;

        var path = Path.Combine(root, leaf);
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// The newest log file per character in a folder. EVE opens a fresh file on every
    /// session change (dock, jump clone), so "current" means most recent per character id.
    /// </summary>
    public static IReadOnlyList<LogFileName> LatestPerCharacter(string folder, DateTime? notBefore = null)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<LogFileName>();

        return Directory.EnumerateFiles(folder, "*.txt")
            .Select(LogFileName.TryParse)
            .Where(f => f is not null)
            .Select(f => f!)
            .Where(f => notBefore is null || f.SessionStart >= notBefore)
            .GroupBy(f => new { f.CharacterId, f.Channel })
            .Select(g => g.OrderByDescending(f => f.SessionStart).First())
            .ToList();
    }
}

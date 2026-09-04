using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace MultiBox.App;

/// <summary>
/// Silent auto-update against this repo's GitHub Releases (CI publishes Velopack
/// packages on every v* tag). Checks on startup and every 6 h; a found update is
/// downloaded in the background (delta when possible) and staged to apply when the
/// app exits — the next launch runs the new version.
///
/// Only active for copies installed via the Velopack Setup.exe; dev and zip runs
/// are never touched. While the repo is private the unauthenticated check fails
/// quietly and starts working once the repo is public.
/// </summary>
internal static class UpdateChecker
{
    private const string RepoUrl = "https://github.com/apettey/multi-box";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    public static void Start()
    {
        _ = Task.Run(async () =>
        {
            UpdateManager mgr;
            try { mgr = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false)); }
            catch { return; }
            if (!mgr.IsInstalled) return;

            await Task.Delay(TimeSpan.FromSeconds(30));
            while (true)
            {
                try
                {
                    var info = await mgr.CheckForUpdatesAsync();
                    if (info is not null)
                    {
                        await mgr.DownloadUpdatesAsync(info);
                        mgr.WaitExitThenApplyUpdates(info, silent: true, restart: false);
                        return; // staged — applies on exit
                    }
                }
                catch
                {
                    // Private repo / offline / rate limit — retry next interval.
                }
                await Task.Delay(CheckInterval);
            }
        });
    }
}

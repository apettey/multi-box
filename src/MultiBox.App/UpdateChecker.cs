using System;
using System.Reflection;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace MultiBox.App;

/// <summary>
/// Silent auto-update against this repo's GitHub Releases (CI publishes Velopack
/// packages on every v* tag). Checks 30 s after startup and then hourly; a found update
/// is downloaded in the background (delta when possible) and announced through
/// <see cref="UpdateReady"/> so the header can offer a restart. If the user never takes
/// it, it is applied when the app exits and the next launch runs the new version.
///
/// Only active for copies installed via the Velopack Setup.exe; dev and zip runs
/// are never touched.
/// </summary>
internal static class UpdateChecker
{
    private const string RepoUrl = "https://github.com/apettey/multi-box";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private static UpdateManager? _manager;
    private static VelopackAsset? _pending;

    /// <summary>The version this process is running, e.g. "1.0.3".</summary>
    public static string CurrentVersion { get; private set; } = AssemblyVersion();

    /// <summary>A downloaded update waiting to be applied, or null.</summary>
    public static string? ReadyVersion => _pending?.Version.ToString();

    /// <summary>Raised on a background thread once an update has finished downloading.</summary>
    public static event Action<string>? UpdateReady;

    public static void Start()
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));
            if (_manager.IsInstalled && _manager.CurrentVersion is { } installed)
                CurrentVersion = installed.ToString();
        }
        catch
        {
            _manager = null;
        }

        if (_manager is not { IsInstalled: true })
            return;

        var manager = _manager;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            while (true)
            {
                try
                {
                    var info = await manager.CheckForUpdatesAsync();
                    if (info is not null)
                    {
                        await manager.DownloadUpdatesAsync(info);
                        _pending = info.TargetFullRelease;
                        UpdateReady?.Invoke(_pending.Version.ToString());
                        return;
                    }
                }
                catch
                {
                    // Offline / rate limit — retry next interval.
                }
                await Task.Delay(CheckInterval);
            }
        });
    }

    /// <summary>Applies the downloaded update now and relaunches into it.</summary>
    public static void RestartToUpdate()
    {
        if (_manager is null || _pending is null)
            return;
        var pending = _pending;
        _pending = null; // the exit hook must not stage it a second time
        _manager.ApplyUpdatesAndRestart(pending);
    }

    /// <summary>Stages a downloaded update to apply once this process exits. Call on shutdown.</summary>
    public static void ApplyOnExit()
    {
        if (_manager is null || _pending is null)
            return;
        try
        {
            _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: false);
        }
        catch
        {
            // Next launch will find and download it again.
        }
    }

    /// <summary>
    /// The version baked in at build time. CI passes -p:Version from the tag; the SDK then
    /// appends "+commit" to the informational version, which is noise in a header.
    /// </summary>
    private static string AssemblyVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info))
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        var plus = info.IndexOf('+');
        return plus < 0 ? info : info[..plus];
    }
}

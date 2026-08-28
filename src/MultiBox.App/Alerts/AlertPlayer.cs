using System.IO;
using System.Media;
using MultiBox.Core.Config;
using MultiBox.Core.Model;

namespace MultiBox.App.Alerts;

/// <summary>
/// Plays one distinct sound per EWAR type, rate-limited per (character, effect).
///
/// The cooldown matters: a scrambler re-logs every activation cycle, so without it a single
/// tackle would fire an alert every few seconds for as long as you were held.
/// </summary>
public sealed class AlertPlayer : IDisposable
{
    private readonly MultiBoxConfig _config;
    private readonly Dictionary<string, SoundPlayer> _players = new();
    private readonly Dictionary<string, DateTime> _lastPlayed = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public AlertPlayer(MultiBoxConfig config) => _config = config;

    public bool Muted { get; set; }

    /// <summary>Plays the alert for an effect landing on a character, honouring the cooldown.</summary>
    public void Play(string characterName, EwarType type)
    {
        if (Muted)
            return;

        if (!_config.Alerts.TryGetValue(type.ToString(), out var setting) || !setting.Enabled)
            return;

        var key = $"{characterName}|{type}";
        lock (_gate)
        {
            if (_lastPlayed.TryGetValue(key, out var last) &&
                DateTime.UtcNow - last < TimeSpan.FromSeconds(setting.CooldownSeconds))
                return;
            _lastPlayed[key] = DateTime.UtcNow;
        }

        try
        {
            GetPlayer(type, setting).Play();
        }
        catch (Exception)
        {
            // A missing sound device must never take the dashboard down.
        }
    }

    private SoundPlayer GetPlayer(EwarType type, AlertSetting setting)
    {
        lock (_gate)
        {
            var cacheKey = $"{type}|{setting.Tone}|{setting.Frequency}|{setting.DurationMs}|{setting.WavPath}";
            if (_players.TryGetValue(cacheKey, out var cached))
                return cached;

            SoundPlayer player;
            if (!string.IsNullOrWhiteSpace(setting.WavPath) && File.Exists(setting.WavPath))
            {
                player = new SoundPlayer(setting.WavPath);
            }
            else
            {
                var wav = ToneGenerator.Build(setting.Tone, setting.Frequency, setting.DurationMs);
                player = new SoundPlayer(new MemoryStream(wav));
            }

            player.Load();
            _players[cacheKey] = player;
            return player;
        }
    }

    /// <summary>Plays a type's sound ignoring the cooldown, for the settings preview button.</summary>
    public void Preview(EwarType type)
    {
        if (_config.Alerts.TryGetValue(type.ToString(), out var setting))
        {
            try { GetPlayer(type, setting).Play(); } catch (Exception) { /* no audio device */ }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var player in _players.Values)
                player.Dispose();
            _players.Clear();
        }
    }
}

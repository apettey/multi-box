using System.Speech.Synthesis;

namespace MultiBox.App.Alerts;

/// <summary>
/// Speaks a short warning when an effect lands that a tone cannot disambiguate.
///
/// A web is the case that motivates this: it is the effect most likely to kill you and the
/// one you can do least about after the fact, and in a ten-client fleet the useful part of
/// the alert is *which pilot*, which a beep cannot carry.
/// </summary>
public sealed class VoiceAnnouncer : IDisposable
{
    private readonly SpeechSynthesizer? _synth;
    private readonly Dictionary<string, DateTime> _lastSpoken = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public VoiceAnnouncer()
    {
        try
        {
            _synth = new SpeechSynthesizer { Rate = 1, Volume = 100 };
            _synth.SetOutputToDefaultAudioDevice();
        }
        catch (Exception)
        {
            // A machine with no voice installed should lose the announcement, not the app.
            _synth = null;
        }
    }

    public bool Enabled { get; set; } = true;

    /// <summary>Minimum gap before the same phrase repeats, so a cycling web does not chatter.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(10);

    public void Say(string phrase)
    {
        if (!Enabled || _synth is null || phrase.Length == 0)
            return;

        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_lastSpoken.TryGetValue(phrase, out var last) && now - last < Cooldown)
                return;
            _lastSpoken[phrase] = now;
        }

        try
        {
            // Cancel first: a stale announcement finishing over a new one is worse than
            // losing it, because the name you hear is then the wrong pilot.
            _synth.SpeakAsyncCancelAll();
            _synth.SpeakAsync(phrase);
        }
        catch (Exception)
        {
            // Losing an announcement must never take the dashboard down mid-fight.
        }
    }

    public void Dispose()
    {
        try { _synth?.Dispose(); }
        catch (Exception) { /* nothing useful to do while shutting down */ }
    }
}

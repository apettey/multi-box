using System.IO;
using System.Text;

namespace MultiBox.App.Alerts;

/// <summary>
/// Builds PCM WAV data in memory so each EWAR type gets a distinguishable sound without
/// shipping audio files. The shapes are chosen to be told apart while you are reading
/// something else: a warble for jams, a falling tone for scrams, two pips for disruption,
/// a low throb for neuts.
/// </summary>
public static class ToneGenerator
{
    private const int SampleRate = 44100;

    public static byte[] Build(string tone, int frequency, int durationMs)
    {
        var sampleCount = SampleRate * durationMs / 1000;
        var samples = new short[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var t = (double)i / SampleRate;
            var progress = (double)i / sampleCount;
            var freq = (double)frequency;
            var gain = 1.0;

            switch (tone.ToLowerInvariant())
            {
                case "warble":
                    // Rapid pitch wobble - reads as "something is wrong with my sensors".
                    freq = frequency + Math.Sin(2 * Math.PI * 18 * t) * frequency * 0.28;
                    break;

                case "descend":
                    // Falling pitch: you are being held down.
                    freq = frequency * (1.6 - 0.8 * progress);
                    break;

                case "double":
                    // Two short pips with a gap between them.
                    gain = progress switch
                    {
                        < 0.34 => 1.0,
                        < 0.5 => 0.0,
                        < 0.84 => 1.0,
                        _ => 0.0
                    };
                    break;

                case "low":
                    // Slow throb at the bottom of the range - capacitor draining.
                    freq = frequency;
                    gain = 0.75 + 0.25 * Math.Sin(2 * Math.PI * 6 * t);
                    break;

                case "buzz":
                    freq = frequency;
                    gain = Math.Sign(Math.Sin(2 * Math.PI * freq * t)) * 0.6;
                    break;
            }

            // Short fade in/out to avoid the click an abruptly started square edge makes.
            var envelope = Math.Min(1.0, Math.Min(progress, 1 - progress) * 25);
            var value = Math.Sin(2 * Math.PI * freq * t) * gain * envelope * 0.42;
            samples[i] = (short)(value * short.MaxValue);
        }

        return WrapWav(samples);
    }

    private static byte[] WrapWav(short[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        var dataBytes = samples.Length * 2;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);                       // PCM chunk size
        writer.Write((short)1);                 // PCM format
        writer.Write((short)1);                 // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);           // byte rate
        writer.Write((short)2);                 // block align
        writer.Write((short)16);                // bits per sample
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);

        foreach (var sample in samples)
            writer.Write(sample);

        writer.Flush();
        return stream.ToArray();
    }
}

using System.Text.RegularExpressions;
using MultiBox.Core.Model;

namespace MultiBox.Core.Parsing;

/// <summary>
/// Parses EVE chat logs. These are UTF-16LE and, unlike game logs, the client re-emits a
/// byte-order mark in front of every appended line, so naive readers see stray U+FEFF
/// characters mid-file. <see cref="Clean"/> strips them.
/// </summary>
public sealed class ChatlogParser
{
    // [ 2026.08.27 21:34:24 ] Red Baldric > amazing boosh lmfao
    private static readonly Regex Line = new(
        @"^﻿?\[ (?<ts>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}) \] (?<sender>.+?) > (?<text>.*)$",
        RegexOptions.Compiled);

    private readonly string _channel;
    private readonly string _listener;

    public ChatlogParser(string channel, string listener)
    {
        _channel = channel;
        _listener = listener;
    }

    public ChatMessage? ParseLine(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
            return null;

        var m = Line.Match(Clean(rawLine));
        if (!m.Success)
            return null;

        if (!DateTime.TryParseExact(m.Groups["ts"].Value, "yyyy.MM.dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var timestamp))
            return null;

        return new ChatMessage
        {
            Timestamp = timestamp,
            Channel = _channel,
            Sender = m.Groups["sender"].Value.Trim(),
            Text = m.Groups["text"].Value.TrimEnd(),
            Listener = _listener
        };
    }

    /// <summary>Removes the per-line BOMs and stray carriage returns EVE writes.</summary>
    public static string Clean(string line) => line.Replace("﻿", string.Empty).TrimEnd('\r');
}

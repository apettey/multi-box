using MultiBox.Core.Model;
using MultiBox.Core.Parsing;
using MultiBox.Core.Stats;
using Xunit;
using Xunit.Abstractions;

namespace MultiBox.Core.Tests;

/// <summary>
/// Runs the parser over the captured logs in full. These assertions are about coverage and
/// plausibility: they catch a regex that silently stops matching a line shape, which unit
/// tests on hand-picked lines would not.
/// </summary>
public class RealLogCorpusTests
{
    private readonly ITestOutputHelper _output;

    public RealLogCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ParsesEveryCombatLineInTheCapturedLogs()
    {
        Assert.NotEmpty(SampleLogs.Gamelogs);

        var unparsed = new List<string>();
        var parsedCount = 0;
        var combatCount = 0;

        foreach (var path in SampleLogs.Gamelogs)
        {
            var header = LogHeader.Parse(File.ReadLines(path));
            Assert.False(string.IsNullOrWhiteSpace(header.Listener));
            var parser = new GamelogParser(header.Listener!);

            foreach (var line in File.ReadLines(path))
            {
                if (!line.Contains("(combat)", StringComparison.Ordinal))
                    continue;
                combatCount++;

                if (parser.ParseLine(line) is not null)
                    parsedCount++;
                else
                    unparsed.Add(line);
            }
        }

        var coverage = (double)parsedCount / combatCount;
        _output.WriteLine($"combat lines: {combatCount}, parsed: {parsedCount} ({coverage:P2})");

        foreach (var sample in unparsed.Take(10))
            _output.WriteLine("UNPARSED: " + EveMarkup.Strip(sample));

        // Misses and damage/EWAR/rep lines are all recognised; anything below this means a
        // shape has been missed entirely.
        Assert.True(coverage > 0.99, $"only {coverage:P2} of combat lines parsed");
    }

    [Fact]
    public void FindsTheEwarEventsFromTheEcmTestSession()
    {
        var byType = new Dictionary<EwarType, int>();

        foreach (var path in SampleLogs.Gamelogs)
        {
            var header = LogHeader.Parse(File.ReadLines(path));
            var parser = new GamelogParser(header.Listener!);

            foreach (var line in File.ReadLines(path))
            {
                var e = parser.ParseLine(line);
                if (e?.Ewar is { } type && e.Direction == Direction.Incoming)
                    byType[type] = byType.GetValueOrDefault(type) + 1;
            }
        }

        foreach (var (type, count) in byType.OrderBy(kv => kv.Key.ToString()))
            _output.WriteLine($"{type}: {count}");

        Assert.True(byType.GetValueOrDefault(EwarType.WarpScramble) > 0, "no incoming warp scrambles found");
        Assert.True(byType.GetValueOrDefault(EwarType.Jam) > 0, "no incoming ECM jams found");
        Assert.True(byType.GetValueOrDefault(EwarType.WarpDisruption) > 0, "no incoming warp disruptions found");
    }

    /// <summary>
    /// Documents the webifier finding: the captured session contains stasis webifier
    /// activity, but only as (notify) range errors from the user's own module. No web is
    /// recorded as landing on anyone, in either direction, by any client.
    /// </summary>
    [Fact]
    public void StasisWebifiersNeverAppearAsCombatEvents()
    {
        var webMentions = 0;
        var webCombatLines = 0;

        foreach (var path in SampleLogs.Gamelogs)
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.Contains("Webifier", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("webif", StringComparison.OrdinalIgnoreCase))
                {
                    webMentions++;
                    if (line.Contains("(combat)", StringComparison.Ordinal))
                        webCombatLines++;
                }
            }
        }

        _output.WriteLine($"lines mentioning a webifier: {webMentions}, of which (combat): {webCombatLines}");
        Assert.True(webMentions > 0, "expected the ECM test session to mention a webifier at all");
        Assert.Equal(0, webCombatLines);
    }

    [Fact]
    public void DedupCollapsesTheSameEventSeenByMultipleClients()
    {
        var all = new List<GameLogEvent>();

        foreach (var path in SampleLogs.Gamelogs)
        {
            var header = LogHeader.Parse(File.ReadLines(path));
            var parser = new GamelogParser(header.Listener!);
            foreach (var line in File.ReadLines(path))
            {
                if (parser.ParseLine(line) is { } e)
                    all.Add(e);
            }
        }

        var scrambles = all.Where(e => e.Ewar == EwarType.WarpScramble).ToList();
        var distinct = scrambles.Select(e => e.DedupKey()).Distinct().Count();

        _output.WriteLine($"scramble lines across all clients: {scrambles.Count}, distinct events: {distinct}");

        Assert.True(scrambles.Count > distinct,
            "expected the same scramble to be logged by more than one client");
    }

    [Fact]
    public void ReplayingALogProducesPlausibleDps()
    {
        var header = LogHeader.Parse(File.ReadLines(SampleLogs.CommanderLog));
        var parser = new GamelogParser(header.Listener!);
        var monitor = new CharacterMonitor(header.Listener!, 1899648001, TimeSpan.FromSeconds(10));

        double peakOut = 0;
        foreach (var line in File.ReadLines(SampleLogs.CommanderLog))
        {
            if (parser.ParseLine(line) is not { } e)
                continue;
            monitor.Apply(e);
            peakOut = Math.Max(peakOut, monitor.DamageOut.PerSecond(e.Timestamp));
        }

        _output.WriteLine($"peak outgoing dps: {peakOut:F1}");
        Assert.True(peakOut > 10, "expected some outgoing damage in a combat session");
        Assert.True(peakOut < 5000, "implausible dps suggests the window or amounts are wrong");
    }
}

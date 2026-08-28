using System.Text;
using MultiBox.Core.Chat;
using MultiBox.Core.Parsing;
using MultiBox.Core.Tailing;
using Xunit;

namespace MultiBox.Core.Tests;

public class ChatDedupTests
{
    [Fact]
    public void ParsesAChatLineAndStripsThePerLineBom()
    {
        var parser = new ChatlogParser("Local", "Lieutent Tyrael");
        // EVE re-emits a BOM in front of every appended line; a naive reader keeps it and
        // the timestamp then fails to match.
        var message = parser.ParseLine("﻿[ 2026.08.27 21:34:24 ] Red Baldric > amazing boosh lmfao");

        Assert.NotNull(message);
        Assert.Equal("Red Baldric", message!.Sender);
        Assert.Equal("amazing boosh lmfao", message.Text);
        Assert.Equal("Local", message.Channel);
        Assert.False(message.IsSystem);
    }

    [Fact]
    public void RecognisesSystemMessages()
    {
        var parser = new ChatlogParser("Local", "Lieutent Tyrael");
        var message = parser.ParseLine("[ 2026.08.27 19:25:28 ] EVE System > Channel changed to Local : J134414");

        Assert.NotNull(message);
        Assert.True(message!.IsSystem);
        Assert.Contains("J134414", message.Text);
    }

    [Fact]
    public void FourClientsLoggingOneFleetMessageProduceOneRow()
    {
        var aggregator = new ChatAggregator();
        const string line = "[ 2026.08.28 16:20:59 ] Hegg Master > warp to me";

        var listeners = new[] { "Commander Tyrael", "Lieutent Tyrael", "Major Tyrael", "Sergeant Tyrael" };
        var emitted = 0;

        foreach (var listener in listeners)
        {
            var message = new ChatlogParser("Fleet", listener).ParseLine(line);
            Assert.NotNull(message);
            if (aggregator.Add(message!) is not null)
                emitted++;
        }

        Assert.Equal(1, emitted);
        Assert.Single(aggregator.Messages);
        // All four are recorded as having seen it, so the UI can show "4".
        Assert.Equal(4, aggregator.Messages.Single().Witnesses.Count);
    }

    [Fact]
    public void IdenticalTextAtDifferentTimesIsNotDeduplicated()
    {
        var aggregator = new ChatAggregator();
        var parser = new ChatlogParser("Fleet", "Commander Tyrael");

        var first = parser.ParseLine("[ 2026.08.28 16:20:59 ] Hegg Master > warp to me");
        var second = parser.ParseLine("[ 2026.08.28 16:21:30 ] Hegg Master > warp to me");

        Assert.NotNull(aggregator.Add(first!));
        Assert.NotNull(aggregator.Add(second!));
        Assert.Equal(2, aggregator.Messages.Count);
    }

    [Fact]
    public void ReadsUtf16ChatLogsThroughTheTailer()
    {
        // Reproduces EVE's on-disk encoding: UTF-16LE with a BOM in front of each line.
        var path = Path.Combine(Path.GetTempPath(), $"Fleet_{Guid.NewGuid():N}.txt");
        try
        {
            var bom = "﻿";
            var content = new StringBuilder();
            content.Append(bom).Append("        Channel Name:    Fleet\r\n");
            content.Append(bom).Append("        Listener:        Commander Tyrael\r\n");
            content.Append(bom).Append("[ 2026.08.28 16:20:59 ] Hegg Master > warp to me\r\n");
            File.WriteAllText(path, content.ToString(), new UnicodeEncoding(false, true));

            using var tailer = new LogTailer(path);
            var lines = tailer.ReadNewLines();

            var header = LogHeader.Parse(lines.Select(ChatlogParser.Clean));
            Assert.Equal("Commander Tyrael", header.Listener);
            Assert.Equal("Fleet", header.ChannelName);

            var parser = new ChatlogParser(header.ChannelName!, header.Listener!);
            var messages = lines.Select(parser.ParseLine).Where(m => m is not null).ToList();
            Assert.Single(messages);
            Assert.Equal("warp to me", messages[0]!.Text);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void TailerReturnsOnlyNewlyAppendedCompleteLines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gamelog_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "first\n");
            using var tailer = new LogTailer(path);
            Assert.Equal(new[] { "first" }, tailer.ReadNewLines());

            // A half-written line must be withheld until its newline arrives.
            File.AppendAllText(path, "second-par");
            Assert.Empty(tailer.ReadNewLines());

            File.AppendAllText(path, "tial\n");
            Assert.Equal(new[] { "second-partial" }, tailer.ReadNewLines());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}

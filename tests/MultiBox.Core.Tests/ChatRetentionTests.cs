using MultiBox.Core.Chat;
using MultiBox.Core.Model;
using Xunit;

namespace MultiBox.Core.Tests;

/// <summary>
/// Ageing chat out of the panel. Local from four hours ago is not comms, it is ballast: it
/// pushes the line you actually want off the panel and keeps the dedup table holding rows
/// nobody will look at again.
/// </summary>
public class ChatRetentionTests
{
    private static readonly DateTime Now = new(2026, 8, 29, 18, 0, 0, DateTimeKind.Utc);

    private static ChatMessage Message(DateTime at, string text, string listener = "Commander Tyrael") => new()
    {
        Timestamp = at,
        Channel = "Local",
        Sender = "Rask Odain",
        Text = text,
        Listener = listener
    };

    [Fact]
    public void MessagesPastTheAgeAreDropped()
    {
        var chat = new ChatAggregator();
        chat.Add(Message(Now.AddMinutes(-45), "ancient"));
        chat.Add(Message(Now.AddMinutes(-31), "old"));
        chat.Add(Message(Now.AddMinutes(-5), "recent"));

        var removed = chat.PruneOlderThan(Now, TimeSpan.FromMinutes(30));

        Assert.Equal(2, removed);
        Assert.Equal(new[] { "recent" }, chat.Messages.Select(m => m.Text));
    }

    [Fact]
    public void NothingOldEnoughMeansNothingRemoved()
    {
        var chat = new ChatAggregator();
        chat.Add(Message(Now.AddMinutes(-5), "recent"));

        Assert.Equal(0, chat.PruneOlderThan(Now, TimeSpan.FromMinutes(30)));
        Assert.Single(chat.Messages);
    }

    [Fact]
    public void PruningAnEmptyPanelIsHarmless()
    {
        Assert.Equal(0, new ChatAggregator().PruneOlderThan(Now, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void AZeroAgeKeepsEverything()
    {
        var chat = new ChatAggregator();
        chat.Add(Message(Now.AddHours(-4), "ancient"));

        // Zero means "no retention limit", not "drop everything".
        Assert.Equal(0, chat.PruneOlderThan(Now, TimeSpan.Zero));
        Assert.Single(chat.Messages);
    }

    [Fact]
    public void APrunedMessageCanArriveAgainFromAnotherClient()
    {
        var chat = new ChatAggregator();
        chat.Add(Message(Now.AddMinutes(-45), "ancient"));
        chat.PruneOlderThan(Now, TimeSpan.FromMinutes(30));

        // Pruning has to forget the dedup key too, or a late copy from a second client would
        // be silently swallowed as a duplicate of a row that no longer exists.
        var again = chat.Add(Message(Now.AddMinutes(-45), "ancient", "Major Tyrael"));

        Assert.NotNull(again);
        Assert.Single(chat.Messages);
    }

    [Fact]
    public void PruningStopsAtTheFirstMessageYoungEnough()
    {
        var chat = new ChatAggregator();
        for (var i = 60; i >= 1; i--)
            chat.Add(Message(Now.AddMinutes(-i), $"m{i}"));

        var removed = chat.PruneOlderThan(Now, TimeSpan.FromMinutes(30));

        // 60..31 go, 30..1 stay.
        Assert.Equal(30, removed);
        Assert.Equal(30, chat.Messages.Count);
        Assert.Equal("m30", chat.Messages.First().Text);
    }

    [Fact]
    public void DeduplicationStillWorksAfterAPrune()
    {
        var chat = new ChatAggregator();
        chat.Add(Message(Now.AddMinutes(-45), "ancient"));
        chat.Add(Message(Now.AddMinutes(-2), "fresh"));
        chat.PruneOlderThan(Now, TimeSpan.FromMinutes(30));

        var duplicate = chat.Add(Message(Now.AddMinutes(-2), "fresh", "Major Tyrael"));

        Assert.Null(duplicate);
        Assert.Equal(2, chat.Messages.First().Witnesses.Count);
    }
}

using System.Windows.Media;
using MultiBox.Core.Chat;

namespace MultiBox.App.ViewModels;

/// <summary>One row in the unified comms panel.</summary>
public sealed class CommsRowViewModel
{
    public CommsRowViewModel(UnifiedMessage message)
    {
        Timestamp = message.Timestamp;
        Time = message.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        Channel = message.Channel;
        ChannelTag = message.Channel.ToUpperInvariant();
        Sender = message.Sender + " › ";
        Text = message.Text;
        ChannelBrush = Palette.ForChannel(message.Channel);

        // How many clients logged this line. Four means everyone was in the channel; one can
        // mean a character was not, which is worth being able to see.
        MergeCount = message.Witnesses.Count;
        IsMerged = MergeCount > 1;
        MergeText = "×" + MergeCount;
        MergeTooltip = "Seen by: " + string.Join(", ", message.Witnesses);
        IsSystem = message.IsSystem;
    }

    public DateTime Timestamp { get; }
    public string Time { get; }
    public string Channel { get; }
    public string ChannelTag { get; }
    public string Sender { get; }
    public string Text { get; }
    public Brush ChannelBrush { get; }
    public int MergeCount { get; }
    public bool IsMerged { get; }
    public string MergeText { get; }
    public string MergeTooltip { get; }
    public bool IsSystem { get; }
}

/// <summary>A channel filter chip.</summary>
public sealed class ChannelChipViewModel : ObservableObject
{
    public ChannelChipViewModel(string name) => Name = name;

    public string Name { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value))
                return;
            Raise(nameof(Foreground));
            Raise(nameof(Background));
            Raise(nameof(BorderBrush));
        }
    }

    public Brush Foreground => _isSelected ? Palette.TextPrimary : Palette.TextMuted;
    public Brush Background => _isSelected ? Palette.Inset : Brushes.Transparent;
    public Brush BorderBrush => _isSelected ? Palette.ActiveBorder : Palette.CardBorder;
}

namespace MultiBox.App.ViewModels;

/// <summary>
/// Anything that takes a slot on the fleet grid: a character card, or a client sitting at
/// character select. Both can carry a thumbnail and be clicked to bring their window forward.
/// </summary>
public abstract class ClientCardViewModel : ObservableObject
{
    /// <summary>Handle of the EVE window behind this card, or zero when it is not running.</summary>
    public IntPtr ClientHandle { get; set; } = IntPtr.Zero;

    private string _hotkey = string.Empty;
    public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }

    /// <summary>
    /// Identifies this card's thumbnail across refreshes. A character keeps its thumbnail
    /// when its window is replaced; a character-select client has no name to key by.
    /// </summary>
    public abstract string ThumbnailKey { get; }
}

/// <summary>
/// An EVE client that is open with no character logged in: just started, or left at
/// character select after a logoff. It has no logs to read, so the card is only a preview
/// and a click target, shown after the character cards.
/// </summary>
public sealed class IdleClientCardViewModel : ClientCardViewModel
{
    public IdleClientCardViewModel(IntPtr handle) => ClientHandle = handle;

    public override string ThumbnailKey => "client:" + ClientHandle.ToInt64();
}

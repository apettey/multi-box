using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MultiBox.App.Interop;
using MultiBox.App.ViewModels;

namespace MultiBox.App;

/// <summary>
/// Draws each running client into its card's thumbnail slot.
///
/// DWM composites a thumbnail over the destination window's client area: it ignores WPF's
/// z-order and does not clip to any element. That is why the slot in the card template is
/// deliberately empty — whatever were drawn there would be covered anyway — and why the
/// rectangles are recomputed from the live visual tree rather than assumed.
/// </summary>
public sealed class ThumbnailOverlay : IDisposable
{
    private readonly Window _window;
    private readonly ItemsControl _host;
    private readonly Dictionary<string, DwmThumbnail> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private IntPtr _handle = IntPtr.Zero;

    public ThumbnailOverlay(Window window, ItemsControl host)
    {
        _window = window;
        _host = host;
    }

    public bool Enabled { get; set; } = true;

    /// <summary>Called once the window has an HWND for DWM to draw into.</summary>
    public void Attach() => _handle = new WindowInteropHelper(_window).Handle;

    /// <summary>
    /// Re-points and repositions every thumbnail. Cheap enough to call on the UI timer, and
    /// it has to be: a card that just gained a client, a resized window and a reordered grid
    /// all change the answer.
    /// </summary>
    public void Sync()
    {
        if (_handle == IntPtr.Zero)
            return;

        if (!Enabled)
        {
            HideAll();
            return;
        }

        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (card, slot) in Slots())
        {
            if (card.ClientHandle == IntPtr.Zero || !slot.IsVisible)
                continue;

            if (!_thumbnails.TryGetValue(card.Name, out var thumbnail))
                _thumbnails[card.Name] = thumbnail = new DwmThumbnail(_handle);

            thumbnail.SetSource(card.ClientHandle);
            Place(thumbnail, slot);
            live.Add(card.Name);
        }

        // A client that closed must lose its thumbnail, otherwise the compositor keeps
        // painting the last frame it saw and the card looks alive when it is not.
        foreach (var name in _thumbnails.Keys.Where(n => !live.Contains(n)).ToList())
        {
            _thumbnails[name].Dispose();
            _thumbnails.Remove(name);
        }
    }

    private void Place(DwmThumbnail thumbnail, FrameworkElement slot)
    {
        if (slot.ActualWidth <= 0 || slot.ActualHeight <= 0)
            return;

        var dpi = VisualTreeHelper.GetDpi(_window);
        var origin = slot.TransformToAncestor(_window).Transform(new Point(0, 0));

        // One pixel in from the slot's own border, so the border stays visible around it.
        var left = (int)Math.Round((origin.X + 1) * dpi.DpiScaleX);
        var top = (int)Math.Round((origin.Y + 1) * dpi.DpiScaleY);
        var right = left + (int)Math.Round((slot.ActualWidth - 2) * dpi.DpiScaleX);
        var bottom = top + (int)Math.Round((slot.ActualHeight - 2) * dpi.DpiScaleY);

        thumbnail.Place(left, top, right, bottom);
    }

    private void HideAll()
    {
        foreach (var thumbnail in _thumbnails.Values)
            thumbnail.Dispose();
        _thumbnails.Clear();
    }

    /// <summary>Every card's thumbnail slot, paired with the card it belongs to.</summary>
    private IEnumerable<(CharacterCardViewModel Card, FrameworkElement Slot)> Slots()
    {
        foreach (var item in _host.Items)
        {
            if (item is not CharacterCardViewModel card)
                continue;

            if (_host.ItemContainerGenerator.ContainerFromItem(item) is not DependencyObject container)
                continue;

            var slot = FindSlot(container);
            if (slot is not null)
                yield return (card, slot);
        }
    }

    /// <summary>
    /// Finds the slot by its Tag rather than by name: names inside a DataTemplate are scoped
    /// per instantiation, so ten cards would all answer to the same one.
    /// </summary>
    private static FrameworkElement? FindSlot(DependencyObject root)
    {
        if (root is FrameworkElement { Tag: "ThumbSlot" } match)
            return match;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindSlot(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }

        return null;
    }

    public void Dispose() => HideAll();
}

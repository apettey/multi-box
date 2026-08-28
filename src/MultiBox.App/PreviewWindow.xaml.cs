using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MultiBox.App.Interop;

namespace MultiBox.App;

/// <summary>
/// A floating live preview of one EVE client, in the style of eve-o-preview: a borderless
/// always-on-top panel showing the client's own window, drawn by the DWM compositor.
///
/// The preview is display only. There is no click-to-focus and no drag-to-arrange of the
/// game window; clicking the panel does nothing to the client. See docs/EULA-COMPLIANCE.md.
/// </summary>
public partial class PreviewWindow : Window
{
    private DwmThumbnail? _thumbnail;
    private IntPtr _pendingSource = IntPtr.Zero;

    public PreviewWindow(string characterName)
    {
        InitializeComponent();
        CharacterName = characterName;
        NameText.Text = characterName;

        SourceInitialized += (_, _) =>
        {
            _thumbnail = new DwmThumbnail(new WindowInteropHelper(this).Handle);
            _thumbnail.SetSource(_pendingSource);
            UpdatePlacement();
        };

        // The destination rectangle is in physical pixels relative to our client area, so it
        // has to be recomputed whenever our size or the monitor's scaling changes.
        SizeChanged += (_, _) => UpdatePlacement();
        DpiChanged += (_, _) => UpdatePlacement();
        Closed += (_, _) => { _thumbnail?.Dispose(); _thumbnail = null; };
    }

    public string CharacterName { get; }

    /// <summary>Points the preview at a client window, or at nothing when it is not running.</summary>
    public void SetSource(IntPtr handle)
    {
        _pendingSource = handle;

        if (_thumbnail is null)
            return;

        _thumbnail.SetSource(handle);
        Placeholder.Visibility = handle == IntPtr.Zero ? Visibility.Visible : Visibility.Collapsed;
        UpdatePlacement();
    }

    /// <summary>Mirrors the dashboard tile's border: blue for the focused client, red under EWAR.</summary>
    public void SetHighlight(bool isForeground, bool underEwar)
    {
        var key = underEwar ? "Danger" : isForeground ? "Accent" : "Line";
        if (TryFindResource(key) is Brush brush)
            Frame.BorderBrush = brush;

        StateText.Text = underEwar ? "EWAR" : isForeground ? "active" : "";
    }

    private void UpdatePlacement()
    {
        if (_thumbnail is null || !_thumbnail.IsRegistered || !IsLoaded)
            return;

        if (ThumbHost.ActualWidth <= 0 || ThumbHost.ActualHeight <= 0)
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var origin = ThumbHost.TransformToAncestor(this).Transform(new Point(0, 0));

        var left = (int)Math.Round(origin.X * dpi.DpiScaleX);
        var top = (int)Math.Round(origin.Y * dpi.DpiScaleY);
        var right = left + (int)Math.Round(ThumbHost.ActualWidth * dpi.DpiScaleX);
        var bottom = top + (int)Math.Round(ThumbHost.ActualHeight * dpi.DpiScaleY);

        _thumbnail.Place(left, top, right, bottom);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Dragging moves this panel only; it never touches the client being previewed.
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}

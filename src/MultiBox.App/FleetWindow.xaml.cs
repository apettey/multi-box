using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MultiBox.App.Interop;
using MultiBox.App.ViewModels;
using MultiBox.Core.Config;

namespace MultiBox.App;

public partial class FleetWindow : Window
{
    private readonly FleetViewModel _viewModel;
    private readonly MultiBoxConfig _config;
    private readonly DispatcherTimer _overlayTimer;
    private readonly HotkeySwitcher _switcher;
    private ThumbnailOverlay? _overlay;
    private CharacterCardViewModel? _dragging;

    public FleetWindow()
    {
        InitializeComponent();

        var configPath = DefaultConfigPath();
        _config = MultiBoxConfig.Load(configPath);

        // On a first run, adopt the layout already arranged in eve-o-preview rather than
        // starting from nothing.
        if (_config.ClientLayout.Count == 0)
            TryImportEveOPreview(_config);

        _viewModel = new FleetViewModel(_config, configPath);
        DataContext = _viewModel;

        RestorePlacement();

        // Thumbnail rectangles depend on layout that does not exist until the tree has been
        // measured, so they are refreshed on a timer rather than computed once.
        _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _overlayTimer.Tick += (_, _) => _overlay?.Sync();

        _switcher = new HotkeySwitcher(_config)
        {
            ResolveClient = _viewModel.HandleFor
        };

        SourceInitialized += (_, _) =>
        {
            _overlay = new ThumbnailOverlay(this, CardHost) { Enabled = _viewModel.ShowThumbnails };
            _overlay.Attach();

            // Hotkeys need the window handle, which does not exist before this point.
            _switcher.Attach(this);
        };

        Loaded += (_, _) =>
        {
            _viewModel.Start();
            _overlayTimer.Start();
        };

        _viewModel.ThumbnailsChanged += () =>
        {
            if (_overlay is not null)
                _overlay.Enabled = _viewModel.ShowThumbnails;
        };

        // A moved or resized window invalidates every rectangle at once.
        SizeChanged += (_, _) => _overlay?.Sync();
        LocationChanged += (_, _) => _overlay?.Sync();
        DpiChanged += (_, _) => _overlay?.Sync();

        Closing += (_, _) =>
        {
            SavePlacement();
            _overlayTimer.Stop();
            _overlay?.Dispose();
            _switcher.Dispose();
            _viewModel.SaveConfig();
            _viewModel.Dispose();
        };
    }

    private static string DefaultConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MultiBox", "multibox.json");

    private static void TryImportEveOPreview(MultiBoxConfig config)
    {
        try
        {
            EveOPreviewImport.TryImport(EveOPreviewImport.DefaultConfigPath, config);
        }
        catch (Exception)
        {
            // An unreadable or differently-shaped config just means no import.
        }
    }

    /// <summary>
    /// Opens on one monitor rather than spanning several. The dashboard is meant to be
    /// glanced at beside the clients, and a window straddling a bezel is unreadable.
    /// </summary>
    private void RestorePlacement()
    {
        var saved = _config.GetPanelLocation("FleetWindow", null, new Pt(int.MinValue, int.MinValue));
        var area = SystemParameters.WorkArea;

        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);

        if (saved.X != int.MinValue)
        {
            Left = saved.X;
            Top = saved.Y;
        }
        else
        {
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }
    }

    private void SavePlacement()
    {
        if (WindowState == WindowState.Normal)
            _config.SetPanelLocation("FleetWindow", null, new Pt((int)Left, (int)Top));
    }

    // --- thumbnail click ------------------------------------------------------------------

    private void Thumbnail_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_config.ClickToFocus)
            return;

        if (sender is FrameworkElement { DataContext: CharacterCardViewModel card })
            ClientFocus.Activate(card.ClientHandle);
    }

    // --- drag to reorder ------------------------------------------------------------------

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        // Starting a drag from the thumbnail would make every click-to-focus a drag attempt.
        if (e.OriginalSource is FrameworkElement { Tag: "ThumbSlot" })
            return;

        if (sender is not FrameworkElement { DataContext: CharacterCardViewModel card })
            return;

        _dragging = card;
        card.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, card, DragDropEffects.Move);
        }
        finally
        {
            card.IsDragging = false;
            _dragging = null;
        }
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = _dragging is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        if (_dragging is null)
            return;

        if (sender is FrameworkElement { DataContext: CharacterCardViewModel target })
            _viewModel.Reorder(_dragging, target);

        e.Handled = true;
    }

    // --- header controls ------------------------------------------------------------------

    private void Squad_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SquadTabViewModel tab })
            _viewModel.ShowSquad(tab.Index);
    }

    private void Channel_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChannelChipViewModel chip })
            _viewModel.SelectChannel(chip);
    }

    private void TestVoice_Click(object sender, MouseButtonEventArgs e) => _viewModel.TestVoice();

    private void CycleGroups_Click(object sender, MouseButtonEventArgs e)
    {
        var editor = new CycleGroupsWindow(_config, _switcher, _viewModel.AllCharacterNames)
        {
            Owner = this
        };

        editor.ShowDialog();

        // Membership changed, so the card tags and the saved config both need catching up.
        _viewModel.RefreshCycleTags();
        _viewModel.SaveConfig();
    }
}

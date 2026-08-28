using System.IO;
using System.Windows;
using System.Windows.Controls;
using MultiBox.App.ViewModels;
using MultiBox.Core.Config;

namespace MultiBox.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _autoScroll = true;

    public MainWindow()
    {
        InitializeComponent();

        var configPath = DefaultConfigPath();
        var config = MultiBoxConfig.Load(configPath);

        // On a first run, adopt the window layout already arranged in eve-o-preview rather
        // than starting from nothing.
        if (config.ClientLayout.Count == 0)
            TryImportEveOPreview(config);

        _viewModel = new MainViewModel(config, configPath);
        DataContext = _viewModel;

        Topmost = config.AlwaysOnTop;
        Opacity = config.Opacity;
        TopmostToggle.IsChecked = config.AlwaysOnTop;

        RestorePosition(config);

        Loaded += (_, _) => _viewModel.Start();
        Closing += (_, _) =>
        {
            SavePosition(config);
            _viewModel.SaveConfig();
            _viewModel.Dispose();
        };

        _viewModel.Chat.CollectionChanged += (_, _) =>
        {
            if (_autoScroll && ChatList.Items.Count > 0)
                ChatList.ScrollIntoView(ChatList.Items[^1]);
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

    private void RestorePosition(MultiBoxConfig config)
    {
        var location = config.GetPanelLocation("MainWindow", null, new Pt(40, 40));
        Left = location.X;
        Top = location.Y;
        Width = config.PanelSize.Width > 0 ? Math.Max(config.PanelSize.Width, 900) : 1180;
        Height = 760;
    }

    private void SavePosition(MultiBoxConfig config)
    {
        config.SetPanelLocation("MainWindow", null, new Pt((int)Left, (int)Top));
        config.AlwaysOnTop = Topmost;
    }

    private void Topmost_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box)
            Topmost = box.IsChecked == true;
    }
}

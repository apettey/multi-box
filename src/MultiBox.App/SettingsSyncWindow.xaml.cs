using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MultiBox.App.ViewModels;
using MultiBox.Core.Config;

namespace MultiBox.App;

/// <summary>
/// Editor for settings sync. Automatic sync is paused while this is open, so a half-made
/// choice of source is never acted on behind the player's back.
/// </summary>
public partial class SettingsSyncWindow : Window
{
    private readonly SettingsSyncViewModel _viewModel;
    private readonly SettingsSyncService _service;
    private readonly DispatcherTimer _clientTimer;

    public SettingsSyncWindow(MultiBoxConfig config, SettingsSyncService service,
        IReadOnlyDictionary<string, long> knownCharacters)
    {
        InitializeComponent();

        _service = service;
        _service.Paused = true;

        _viewModel = new SettingsSyncViewModel(config, service, knownCharacters);
        DataContext = _viewModel;

        // Clients open and close while the window is up, and the sync button follows them.
        _clientTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clientTimer.Tick += (_, _) => _viewModel.RefreshClients();
        _clientTimer.Start();

        Closed += (_, _) =>
        {
            _clientTimer.Stop();
            _service.Paused = false;
        };
    }

    private void Sync_Click(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.CanSync)
            _viewModel.SyncNow();
    }

    private void Restore_Click(object sender, MouseButtonEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Put back every character file in this profile as it was before the most recent sync?",
            "Restore backup", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK)
            _viewModel.RestoreLatest();
    }

    private void Close_Click(object sender, MouseButtonEventArgs e) => Close();
}

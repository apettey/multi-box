using System.Windows;
using System.Windows.Input;
using MultiBox.App.Interop;
using MultiBox.App.ViewModels;
using MultiBox.Core.Config;

namespace MultiBox.App;

/// <summary>
/// Editor for the Fast Screen Switcher groups. Changes are written straight into the shared
/// config object and re-registered as you make them, so a hotkey can be tried the moment it
/// is typed rather than after a restart.
/// </summary>
public partial class CycleGroupsWindow : Window
{
    private readonly MultiBoxConfig _config;
    private readonly HotkeySwitcher _switcher;
    private readonly List<CycleGroupViewModel> _groups = new();

    public CycleGroupsWindow(MultiBoxConfig config, HotkeySwitcher switcher, IEnumerable<string> characters)
    {
        InitializeComponent();

        _config = config;
        _switcher = switcher;
        _config.EnsureCycleGroups();

        var roster = characters.ToList();
        for (var i = 0; i < _config.CycleGroups.Count; i++)
        {
            var vm = new CycleGroupViewModel(i, _config.CycleGroups[i], roster);
            vm.Changed += ReRegister;
            _groups.Add(vm);
        }

        GroupList.ItemsSource = _groups;
        ShowRejected();
    }

    private void ReRegister()
    {
        _switcher.Register();
        ShowRejected();
    }

    /// <summary>
    /// Windows refuses a hotkey another process already owns, and silently doing nothing
    /// afterwards is the worst possible outcome, so say which ones failed.
    /// </summary>
    private void ShowRejected()
    {
        if (_switcher.Rejected.Count == 0)
        {
            RejectedNote.Visibility = Visibility.Collapsed;
            return;
        }

        RejectedNote.Text = "Windows refused these hotkeys, most likely because another " +
                            "application already claims them: " + string.Join(", ", _switcher.Rejected);
        RejectedNote.Visibility = Visibility.Visible;
    }

    private void Member_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MemberChipViewModel chip })
            chip.Click();
    }

    private void Import_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (!EveOPreviewImport.TryImport(EveOPreviewImport.DefaultConfigPath, _config))
            {
                MessageBox.Show(this,
                    "No EVE-O Preview config found at\n" + EveOPreviewImport.DefaultConfigPath,
                    "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not read that config:\n" + ex.Message,
                "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var group in _groups)
            group.SyncFromGroup();

        ReRegister();
    }

    private void Close_Click(object sender, MouseButtonEventArgs e) => Close();
}

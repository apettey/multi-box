using System.Windows;
using System.Windows.Threading;
using MultiBox.App.Interop;
using MultiBox.App.ViewModels;
using MultiBox.Core.Config;

namespace MultiBox.App;

/// <summary>
/// Owns one <see cref="PreviewWindow"/> per running EVE client and keeps them pointed at the
/// right window handles.
///
/// Positions are stored under the same config keys eve-o-preview uses ("EVE - Character
/// Name" in FlatLayout, or PerClientLayout when per-client arrangements are enabled), so an
/// imported layout puts the panels where they already were.
/// </summary>
public sealed class PreviewManager : IDisposable
{
    private readonly MultiBoxConfig _config;
    private readonly MainViewModel _viewModel;
    private readonly Window _owner;
    private readonly Dictionary<string, PreviewWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer;
    private bool _enabled;

    public PreviewManager(MultiBoxConfig config, MainViewModel viewModel, Window owner)
    {
        _config = config;
        _viewModel = viewModel;
        _owner = owner;

        // Clients come and go on undock/login; a couple of seconds is responsive enough
        // without enumerating every window on the desktop constantly.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Sync();
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;

            if (value)
            {
                Sync();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                CloseAll();
            }
        }
    }

    private void Sync()
    {
        if (!_enabled)
            return;

        IReadOnlyList<EveClientWindow> clients;
        try
        {
            clients = EveClientLocator.FindClients();
        }
        catch (Exception)
        {
            // Window enumeration is best-effort; previews simply do not update this round.
            return;
        }

        foreach (var client in clients)
        {
            var window = Ensure(client.CharacterName);
            window.SetSource(client.Handle);

            var character = _viewModel.Characters.FirstOrDefault(c =>
                c.Name.Equals(client.CharacterName, StringComparison.OrdinalIgnoreCase));
            window.SetHighlight(client.IsForeground, character?.UnderEwar ?? false);
        }

        // A client that closed leaves its panel behind showing a frozen last frame, which is
        // worse than no panel at all, so drop it.
        var live = clients.Select(c => c.CharacterName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _windows.Keys.Where(n => !live.Contains(n)).ToList())
            Close(name);
    }

    private PreviewWindow Ensure(string characterName)
    {
        if (_windows.TryGetValue(characterName, out var existing))
            return existing;

        var index = _windows.Count;
        var window = new PreviewWindow(characterName)
        {
            // Owned rather than free-floating: two topmost windows are ordered by whichever
            // was activated last, so an unowned panel disappears behind the dashboard the
            // first time the dashboard is clicked. An owner also closes them together.
            Owner = _owner,
            Width = Math.Max(_config.PanelSize.Width, 120),
            Height = Math.Max(_config.PanelSize.Height, 80)
        };

        var key = MultiBoxConfig.ClientKey(characterName);
        var location = _config.GetPanelLocation(key, null, DefaultLocation(index, window.Width, window.Height));
        window.Left = location.X;
        window.Top = location.Y;

        _windows[characterName] = window;
        window.Show();
        return window;
    }

    /// <summary>
    /// A row along the bottom of the primary work area. The dashboard opens at the top left,
    /// so anything starting there would be born underneath it.
    /// </summary>
    private static Pt DefaultLocation(int index, double width, double height)
    {
        var area = SystemParameters.WorkArea;
        return new Pt(
            (int)(area.Left + 12 + index * (width + 8)),
            (int)(area.Bottom - height - 12));
    }

    private void Close(string characterName)
    {
        if (!_windows.Remove(characterName, out var window))
            return;
        Remember(window);
        window.Close();
    }

    private void CloseAll()
    {
        foreach (var window in _windows.Values)
        {
            Remember(window);
            window.Close();
        }
        _windows.Clear();
    }

    /// <summary>Writes a panel's position back to the config so it reopens where it was left.</summary>
    private void Remember(PreviewWindow window)
    {
        var key = MultiBoxConfig.ClientKey(window.CharacterName);
        _config.SetPanelLocation(key, null, new Pt((int)window.Left, (int)window.Top));

        if (window.Width > 0 && window.Height > 0)
            _config.PanelSize = new Sz((int)window.Width, (int)window.Height);
    }

    public void Dispose()
    {
        _timer.Stop();
        CloseAll();
    }
}

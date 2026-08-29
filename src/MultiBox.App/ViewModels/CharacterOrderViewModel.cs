using System.Collections.ObjectModel;
using System.Windows.Media;
using MultiBox.Core.Config;

namespace MultiBox.App.ViewModels;

/// <summary>
/// Reordering for a fleet too large to arrange by dragging cards around the grid.
///
/// Order is fleet-wide rather than per squad: squads are simply consecutive slices of it, so
/// moving a character past a squad boundary is how you move them between squads. The rows
/// show which squad each character currently lands in, because that is the consequence of a
/// move that is otherwise invisible.
/// </summary>
public sealed class CharacterOrderViewModel : ObservableObject
{
    private readonly MultiBoxConfig _config;

    public CharacterOrderViewModel(MultiBoxConfig config, IEnumerable<(string Name, bool Running)> characters)
    {
        _config = config;

        foreach (var (name, running) in characters)
            Rows.Add(new OrderRowViewModel(name, running));

        Renumber();
    }

    public ObservableCollection<OrderRowViewModel> Rows { get; } = new();

    private OrderRowViewModel? _selected;
    public OrderRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
                return;
            Raise(nameof(CanMoveUp));
            Raise(nameof(CanMoveDown));
        }
    }

    public bool CanMoveUp => _selected is not null && Rows.IndexOf(_selected) > 0;
    public bool CanMoveDown => _selected is not null && Rows.IndexOf(_selected) < Rows.Count - 1;

    public int SquadSize
    {
        get => _config.SquadSize;
        set
        {
            var clamped = Math.Clamp(value, 1, 20);
            if (_config.SquadSize == clamped)
                return;
            _config.SquadSize = clamped;
            Raise(nameof(SquadSize));
            Raise(nameof(Summary));
            Renumber();
        }
    }

    public string Summary
    {
        get
        {
            var running = Rows.Count(r => r.IsRunning);
            var squads = FleetLayout.SquadCount(
                _config.ShowOnlyRunningClients ? running : Rows.Count, _config.SquadSize);
            return $"{Rows.Count} characters · {running} with a client open · {squads} squad(s) " +
                   $"of {_config.SquadSize}";
        }
    }

    public void Move(int delta)
    {
        if (_selected is null)
            return;

        var from = Rows.IndexOf(_selected);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Rows.Count)
            return;

        Rows.Move(from, to);
        Renumber();

        // Keep the moved row selected so a run of presses walks it up the list.
        Selected = Rows[to];
        Raise(nameof(CanMoveUp));
        Raise(nameof(CanMoveDown));
    }

    /// <summary>Sorts so every character with an open client comes first, order preserved.</summary>
    public void RunningFirst()
    {
        var sorted = Rows.OrderByDescending(r => r.IsRunning).ToList();
        Rows.Clear();
        foreach (var row in sorted)
            Rows.Add(row);
        Renumber();
    }

    public IReadOnlyList<string> Order => Rows.Select(r => r.Name).ToList();

    /// <summary>
    /// Recomputes the position and squad label on every row. Squad membership depends on the
    /// visibility rule: with closed clients hidden, only open ones take up a slot.
    /// </summary>
    private void Renumber()
    {
        var size = Math.Max(1, _config.SquadSize);
        var slot = 0;

        foreach (var row in Rows)
        {
            var counts = !_config.ShowOnlyRunningClients || row.IsRunning;
            row.Update(
                position: Rows.IndexOf(row) + 1,
                squad: counts ? (slot / size) + 1 : null);

            if (counts)
                slot++;
        }

        Raise(nameof(Summary));
    }
}

/// <summary>One character in the ordering list.</summary>
public sealed class OrderRowViewModel : ObservableObject
{
    public OrderRowViewModel(string name, bool running)
    {
        Name = name;
        IsRunning = running;
    }

    public string Name { get; }
    public bool IsRunning { get; }

    public string StateText => IsRunning ? "open" : "closed";
    public Brush StateBrush => IsRunning ? Palette.Success : Palette.TextDisabled;
    public Brush NameBrush => IsRunning ? Palette.TextPrimary : Palette.TextMuted;

    private int _position;
    public string Position => _position.ToString();

    private int? _squad;

    /// <summary>Which squad tab this character lands on, or a dash when it is not shown.</summary>
    public string SquadText => _squad is null ? "—" : "SQUAD " + _squad;

    public Brush SquadBrush => _squad is null ? Palette.TextDisabled : Palette.NeutBlue;

    internal void Update(int position, int? squad)
    {
        _position = position;
        _squad = squad;
        Raise(nameof(Position));
        Raise(nameof(SquadText));
        Raise(nameof(SquadBrush));
    }
}

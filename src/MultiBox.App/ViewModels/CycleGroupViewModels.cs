using System.Collections.ObjectModel;
using System.Windows.Media;
using MultiBox.Core.Config;

namespace MultiBox.App.ViewModels;

/// <summary>One cycle group in the config window.</summary>
public sealed class CycleGroupViewModel : ObservableObject
{
    private readonly CycleGroup _group;

    public CycleGroupViewModel(int index, CycleGroup group, IEnumerable<string> allCharacters)
    {
        Index = index;
        _group = group;
        Title = $"GROUP {index + 1}";

        foreach (var name in allCharacters)
        {
            var chip = new MemberChipViewModel(name);
            chip.Clicked += () => Toggle(chip);
            Roster.Add(chip);
        }

        SyncFromGroup();
    }

    public int Index { get; }
    public string Title { get; }
    public ObservableCollection<MemberChipViewModel> Roster { get; } = new();

    public string ForwardHotkey
    {
        get => string.Join(", ", _group.ForwardHotkeys);
        set
        {
            _group.ForwardHotkeys = SplitKeys(value);
            Raise(nameof(ForwardHotkey));
            Changed?.Invoke();
        }
    }

    public string BackwardHotkey
    {
        get => string.Join(", ", _group.BackwardHotkeys);
        set
        {
            _group.BackwardHotkeys = SplitKeys(value);
            Raise(nameof(BackwardHotkey));
            Changed?.Invoke();
        }
    }

    /// <summary>The cycle order spelled out, so it can be read without counting chips.</summary>
    public string OrderLine => _group.Members.Count == 0
        ? "no members"
        : "order: " + string.Join(" → ", _group.Members);

    public event Action? Changed;

    private static List<string> SplitKeys(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void Toggle(MemberChipViewModel chip)
    {
        var existing = _group.Members.FindIndex(m => m.Equals(chip.Name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
            _group.Members.RemoveAt(existing);
        else
            _group.Members.Add(chip.Name);

        SyncFromGroup();
        Changed?.Invoke();
    }

    /// <summary>Repaints every chip's membership and position from the underlying group.</summary>
    public void SyncFromGroup()
    {
        foreach (var chip in Roster)
        {
            var index = _group.Members.FindIndex(m => m.Equals(chip.Name, StringComparison.OrdinalIgnoreCase));
            chip.SetPosition(index < 0 ? 0 : index + 1);
        }

        Raise(nameof(OrderLine));
        Raise(nameof(ForwardHotkey));
        Raise(nameof(BackwardHotkey));
    }
}

/// <summary>A clickable character chip inside a cycle group's roster.</summary>
public sealed class MemberChipViewModel : ObservableObject
{
    public MemberChipViewModel(string name) => Name = name;

    public string Name { get; }

    public event Action? Clicked;

    public void Click() => Clicked?.Invoke();

    private int _position;

    /// <summary>1-based place in the cycle, or 0 when this character is not a member.</summary>
    public string Position => _position.ToString();

    public bool IsMember => _position > 0;

    public Brush Foreground => IsMember ? Palette.TextPrimary : Palette.TextMuted;
    public Brush Background => IsMember ? Palette.Wash(Palette.EwarWebBase) : Brushes.Transparent;
    public Brush BorderBrush => IsMember ? Palette.NeutBlue : Palette.CardBorder;

    internal void SetPosition(int position)
    {
        if (_position == position)
            return;
        _position = position;
        Raise(nameof(Position));
        Raise(nameof(IsMember));
        Raise(nameof(Foreground));
        Raise(nameof(Background));
        Raise(nameof(BorderBrush));
    }
}

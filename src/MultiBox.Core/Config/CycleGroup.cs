namespace MultiBox.Core.Config;

/// <summary>
/// One Fast Screen Switcher group: a hotkey pair and an ordered ring of characters it walks.
///
/// The shape mirrors eve-o-preview's, because the point is to be able to import a layout
/// somebody has already tuned rather than make them rebuild it. eve-o stores each group as
/// three flat keys — CycleGroup1ForwardHotkeys, CycleGroup1BackwardHotkeys,
/// CycleGroup1ClientsOrder — and keys the member list by window title.
/// </summary>
public sealed class CycleGroup
{
    /// <summary>Hotkeys that step forward through <see cref="Members"/>, e.g. "F13", "Control+F13".</summary>
    public List<string> ForwardHotkeys { get; set; } = new();

    /// <summary>Optional; a group is usable with forward only.</summary>
    public List<string> BackwardHotkeys { get; set; } = new();

    /// <summary>Character names in cycle order. A character may appear in several groups.</summary>
    public List<string> Members { get; set; } = new();

    public bool IsConfigured => Members.Count > 0 && ForwardHotkeys.Count > 0;

    /// <summary>
    /// The member after <paramref name="current"/>, wrapping, skipping anyone whose client is
    /// not running. Returns null when the group has no runnable member.
    /// </summary>
    public string? Step(string? current, Func<string, bool> isRunning, bool forward)
    {
        if (Members.Count == 0)
            return null;

        var start = current is null
            ? -1
            : Members.FindIndex(m => m.Equals(current, StringComparison.OrdinalIgnoreCase));

        // Walk the whole ring once. Starting from -1 means "not currently on this group",
        // in which case forward should land on the first member rather than the second.
        for (var hop = 1; hop <= Members.Count; hop++)
        {
            var offset = forward ? hop : -hop;
            var index = ((start + offset) % Members.Count + Members.Count) % Members.Count;
            var candidate = Members[index];
            if (isRunning(candidate))
                return candidate;
        }

        return null;
    }
}

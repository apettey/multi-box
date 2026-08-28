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
    ///
    /// A null <paramref name="current"/> means "not currently anywhere in this group", and
    /// enters the ring at the end you are heading away from: the first member going forward,
    /// the last going backward. Both directions then reach every member in one pass.
    /// </summary>
    public string? Step(string? current, Func<string, bool> isRunning, bool forward)
    {
        if (Members.Count == 0)
            return null;

        var start = current is null
            ? -1
            : Members.FindIndex(m => m.Equals(current, StringComparison.OrdinalIgnoreCase));

        // Walk the whole ring once, taking the first candidate that is actually running.
        for (var hop = 0; hop < Members.Count; hop++)
        {
            var index = start < 0
                ? (forward ? hop : Members.Count - 1 - hop)
                : ((start + (forward ? hop + 1 : -(hop + 1))) % Members.Count + Members.Count) % Members.Count;

            if (isRunning(Members[index]))
                return Members[index];
        }

        return null;
    }
}

namespace MultiBox.Core.Config;

/// <summary>
/// Remembers where each cycle press left off.
///
/// It deliberately tracks only *one* position, for the group used last, rather than a
/// position per group. Switching to another group and coming back restarts the first group
/// from its beginning, which is what pressing a group's hotkey after using a different one
/// is understood to mean: "take me to the top of this group", not "resume something I was
/// part-way through several actions ago". Per-group memory makes the same keypress land on a
/// different client depending on history you can no longer see.
/// </summary>
public sealed class CycleNavigator
{
    private int? _lastGroup;
    private string? _lastRaised;

    /// <summary>The character whose client should be raised next, or null if none can be.</summary>
    public string? Next(IReadOnlyList<CycleGroup> groups, int groupIndex,
        Func<string, bool> isRunning, bool forward)
    {
        if (groupIndex < 0 || groupIndex >= groups.Count)
            return null;

        // A different group than last time means starting afresh.
        var current = _lastGroup == groupIndex ? _lastRaised : null;

        var next = groups[groupIndex].Step(current, isRunning, forward);
        if (next is null)
            return null;

        _lastGroup = groupIndex;
        _lastRaised = next;
        return next;
    }

    /// <summary>
    /// Forgets the current position, so the next press of any group starts at its beginning.
    /// Used when the group definitions change underneath us.
    /// </summary>
    public void Reset()
    {
        _lastGroup = null;
        _lastRaised = null;
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MultiBox.Core.Config;

namespace MultiBox.App.Interop;

/// <summary>
/// The Fast Screen Switcher: global hotkeys that step the foreground through a cycle group's
/// clients, the way eve-o-preview does.
///
/// This uses RegisterHotKey rather than a keyboard hook, and the difference matters. A hook
/// would see every keystroke on the machine, including those you type into the game; this
/// asks Windows to reserve specific combinations and tell us when they fire, and it can
/// observe nothing else. It also cannot send anything: a hotkey moves window focus, and the
/// keys you press afterwards go to the game because it is the focused window, not because
/// this forwarded them. Input broadcasting remains impossible here.
///
/// See docs/EULA-COMPLIANCE.md.
/// </summary>
public sealed class HotkeySwitcher : IDisposable
{
    private const int WmHotkey = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [Flags]
    private enum Mods : uint
    {
        Alt = 0x1,
        Control = 0x2,
        Shift = 0x4,
        Win = 0x8,
        NoRepeat = 0x4000
    }

    private readonly record struct Binding(int GroupIndex, bool Forward);

    private readonly Dictionary<int, Binding> _bindings = new();
    private readonly MultiBoxConfig _config;
    private IntPtr _handle = IntPtr.Zero;
    private HwndSource? _source;
    private int _nextId = 1;

    public HotkeySwitcher(MultiBoxConfig config) => _config = config;

    /// <summary>Where the last press left off. Owns the restart-on-group-change rule.</summary>
    private readonly CycleNavigator _navigator = new();

    /// <summary>Asked for each candidate: is this character's client actually running?</summary>
    public Func<string, IntPtr>? ResolveClient { get; set; }

    /// <summary>Raised after a successful switch, so the dashboard can highlight the card.</summary>
    public event Action<string>? Switched;

    /// <summary>Hotkeys that Windows refused, usually because something else owns them.</summary>
    public IReadOnlyList<string> Rejected => _rejected;
    private readonly List<string> _rejected = new();

    public void Attach(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
        Register();
    }

    /// <summary>Re-reads the config and re-registers everything. Call after editing groups.</summary>
    public void Register()
    {
        Unregister();

        // Membership may have changed, so a remembered position no longer means anything.
        _navigator.Reset();

        if (_handle == IntPtr.Zero)
            return;

        _config.EnsureCycleGroups();

        for (var i = 0; i < _config.CycleGroups.Count; i++)
        {
            var group = _config.CycleGroups[i];
            foreach (var key in group.ForwardHotkeys)
                Bind(key, new Binding(i, true));
            foreach (var key in group.BackwardHotkeys)
                Bind(key, new Binding(i, false));
        }
    }

    private void Bind(string hotkey, Binding binding)
    {
        if (!TryParse(hotkey, out var mods, out var vk))
            return;

        var id = _nextId++;
        // NoRepeat: held down, a cycle hotkey would otherwise sweep the whole fleet.
        if (RegisterHotKey(_handle, id, mods | (uint)Mods.NoRepeat, vk))
            _bindings[id] = binding;
        else
            _rejected.Add(hotkey);
    }

    private void Unregister()
    {
        foreach (var id in _bindings.Keys)
            UnregisterHotKey(_handle, id);
        _bindings.Clear();
        _rejected.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey || !_bindings.TryGetValue(wParam.ToInt32(), out var binding))
            return IntPtr.Zero;

        Cycle(binding);
        handled = true;
        return IntPtr.Zero;
    }

    private void Cycle(Binding binding)
    {
        if (binding.GroupIndex >= _config.CycleGroups.Count)
            return;

        var next = _navigator.Next(_config.CycleGroups, binding.GroupIndex,
            name => Resolve(name) != IntPtr.Zero, binding.Forward);

        if (next is null || !ClientFocus.Activate(Resolve(next)))
            return;

        Switched?.Invoke(next);
    }

    private IntPtr Resolve(string character) => ResolveClient?.Invoke(character) ?? IntPtr.Zero;

    /// <summary>
    /// Parses eve-o-preview's hotkey strings: "F13", "Control+F13", "Shift+Alt+D". The
    /// modifier spellings are the ones eve-o's own config uses, so anything it can save,
    /// this can read.
    /// </summary>
    public static bool TryParse(string hotkey, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(hotkey))
            return false;

        var mods = (Mods)0;
        Key? key = null;

        foreach (var raw in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL": mods |= Mods.Control; continue;
                case "ALT": mods |= Mods.Alt; continue;
                case "SHIFT": mods |= Mods.Shift; continue;
                case "WIN":
                case "WINDOWS": mods |= Mods.Win; continue;
            }

            if (Enum.TryParse<Key>(raw, ignoreCase: true, out var parsed))
                key = parsed;
            else
                return false;
        }

        if (key is null)
            return false;

        modifiers = (uint)mods;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return virtualKey != 0;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}

using System.Runtime.InteropServices;

namespace MultiBox.App.Interop;

/// <summary>
/// The Desktop Window Manager thumbnail API — the same mechanism eve-o-preview, Alt-Tab and
/// the taskbar hover previews use to show a live view of another window.
///
/// This is emphatically *not* screen capture, and the distinction is the whole reason it is
/// allowed to exist in this codebase. The application hands DWM a source window handle and a
/// destination rectangle; the compositor then draws the preview itself, directly to the
/// screen. No pixel data is ever returned to this process — there is nothing here to read,
/// sample, or run OCR over, and no buffer that could be inspected. None of the capture
/// calls that <see cref="NativeMethods"/> lists as forbidden appear here either: drawing a
/// thumbnail needs none of them.
///
/// Registration is one-way and read-only with respect to the source: a thumbnail cannot
/// move, focus, resize or send anything to the window it previews.
///
/// See docs/EULA-COMPLIANCE.md.
/// </summary>
internal static class DwmApi
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Cx, Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ThumbnailProperties
    {
        public int Flags;
        public RECT Destination;
        public RECT Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    public const int FlagDestination = 0x00000001;
    public const int FlagOpacity = 0x00000004;
    public const int FlagVisible = 0x00000008;
    public const int FlagClientAreaOnly = 0x00000010;

    [DllImport("dwmapi.dll")]
    public static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    public static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref ThumbnailProperties properties);

    [DllImport("dwmapi.dll")]
    public static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out SIZE size);
}

/// <summary>
/// One live thumbnail: a registration binding a source window to a rectangle inside one of
/// our own windows. Re-pointing at a new source (a client that restarted, so its handle
/// changed) is a re-registration, which is why <see cref="SetSource"/> exists rather than a
/// settable property.
/// </summary>
internal sealed class DwmThumbnail : IDisposable
{
    private readonly IntPtr _destination;
    private IntPtr _handle = IntPtr.Zero;
    private IntPtr _source = IntPtr.Zero;

    public DwmThumbnail(IntPtr destination) => _destination = destination;

    public bool IsRegistered => _handle != IntPtr.Zero;

    public IntPtr Source => _source;

    /// <summary>Aspect ratio of the source window, or null while nothing is registered.</summary>
    public double? SourceAspect
    {
        get
        {
            if (!IsRegistered || DwmApi.DwmQueryThumbnailSourceSize(_handle, out var size) != 0)
                return null;
            return size.Cy > 0 ? (double)size.Cx / size.Cy : null;
        }
    }

    public void SetSource(IntPtr source)
    {
        if (source == _source && IsRegistered)
            return;

        Unregister();
        _source = source;

        if (source == IntPtr.Zero || _destination == IntPtr.Zero)
            return;

        // A source that vanished between enumeration and here fails harmlessly; the next
        // rescan re-registers it.
        if (DwmApi.DwmRegisterThumbnail(_destination, source, out var handle) == 0)
            _handle = handle;
    }

    /// <summary>Places the preview at a rectangle given in physical pixels, relative to the
    /// destination window's client area.</summary>
    public void Place(int left, int top, int right, int bottom)
    {
        if (!IsRegistered || right <= left || bottom <= top)
            return;

        var properties = new DwmApi.ThumbnailProperties
        {
            Flags = DwmApi.FlagDestination | DwmApi.FlagVisible | DwmApi.FlagOpacity |
                    DwmApi.FlagClientAreaOnly,
            Destination = new DwmApi.RECT { Left = left, Top = top, Right = right, Bottom = bottom },
            Opacity = 255,
            Visible = true,
            // Excludes the client's title bar and borders, so the preview is game only.
            SourceClientAreaOnly = true
        };

        DwmApi.DwmUpdateThumbnailProperties(_handle, ref properties);
    }

    private void Unregister()
    {
        if (_handle == IntPtr.Zero)
            return;
        DwmApi.DwmUnregisterThumbnail(_handle);
        _handle = IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source = IntPtr.Zero;
    }
}

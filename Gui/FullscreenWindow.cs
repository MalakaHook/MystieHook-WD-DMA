using System.Runtime.InteropServices;
using Raylib_cs;

namespace MystieHook.Gui;

// A normal, non-topmost borderless window: Start/Alt-Tab retain normal Windows behavior.
internal static class FullscreenWindow
{
    private static int _monitor;
    private static long _nextCheck;
    private static bool _wasFocused;

    internal static void SelectMonitor(int monitor)
    {
        _monitor = monitor;
        if (Raylib.IsWindowFullscreen()) Raylib.ToggleFullscreen();
        Raylib.SetWindowState(ConfigFlags.UndecoratedWindow);
        Update(true);
    }

    internal static unsafe void Update(bool force = false)
    {
        bool focused = Raylib.IsWindowFocused();
        bool regainedFocus = focused && !_wasFocused;
        _wasFocused = focused;
        long now = Environment.TickCount64;
        if (!force && !regainedFocus && now < _nextCheck) return;
        _nextCheck = now + 250;
        nint window = (nint)Raylib.GetWindowHandle();
        if (window == 0 || IsIconic(window)) return;
        int count = Raylib.GetMonitorCount();
        if (count <= 0) return;
        _monitor = Math.Clamp(_monitor, 0, count - 1);
        var position = Raylib.GetMonitorPosition(_monitor);
        var point = new Point { X = (int)position.X + Raylib.GetMonitorWidth(_monitor) / 2,
            Y = (int)position.Y + Raylib.GetMonitorHeight(_monitor) / 2 };
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromPoint(point, 2), ref info)) return;
        // Monitor bounds, deliberately excluding the taskbar-dependent work area.
        Rect bounds = info.Monitor;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return;
        long style = GetWindowLongPtrW(window, -16).ToInt64();
        const long decorations = 0x00C00000L | 0x00040000L | 0x01000000L; // caption, resize frame, maximized
        bool restyle = (style & decorations) != 0;
        GetWindowRect(window, out Rect current);
        if (restyle) SetWindowLongPtrW(window, -16, (nint)(style & ~decorations));
        if (force || restyle || current.Left != bounds.Left || current.Top != bounds.Top
            || current.Right != bounds.Right || current.Bottom != bounds.Bottom)
        {
            // Do not activate or reorder the window when Windows changes display metrics.
            SetWindowPos(window, 0, bounds.Left, bounds.Top, width, height, 0x0004 | 0x0010 | 0x0020);
        }
        ScreenService.UpdateFromMonitor(_monitor);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}

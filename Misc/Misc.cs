using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;
using Raylib_cs;
using static MystieHook.OverlayWindow;

namespace MystieHook
{
    public static class Misc
    {
        private static bool _undecoratedApplied = false;
        private const uint WDA_NONE = 0x0;                  // allow capture
        private const uint WDA_MONITOR = 0x1;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;   // blocks capture
        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_FRAMECHANGED = 0x0020;

        private const long WS_EX_NOREDIRECTIONBITMAP = 0x00200000L;

        [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(nint hWnd, out uint dwAffinity);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        public static unsafe void AllowCapture()
        {
            try
            {
                nint hwnd = (nint)Raylib.GetWindowHandle(); // Raylib_cs exposes this on Windows
                if (hwnd != IntPtr.Zero)
                    SetWindowDisplayAffinity(hwnd, WDA_NONE);
            }
            catch { /* ignore */ }
        }

        public static unsafe void ApplyAll()
        {
            nint hwnd = (nint)Raylib.GetWindowHandle();
            if (hwnd == IntPtr.Zero) return;

            try
            {
                // 1) Ensure display affinity allows capture
                SetWindowDisplayAffinity(hwnd, WDA_NONE);

                // 2) Clear NOREDIRECTIONBITMAP so DWM can redirect (otherwise RDP/AnyDesk/OBS may see black)
                var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
                long exVal = ex.ToInt64();
                if ((exVal & WS_EX_NOREDIRECTIONBITMAP) != 0)
                {
                    exVal &= ~WS_EX_NOREDIRECTIONBITMAP;
                    SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)exVal);
                    // Tell the window manager styles changed
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                }
            }
            catch { /* ignore */ }
        }

        public static unsafe bool IsExcludedFromCapture()
        {
            nint hwnd = (nint)Raylib.GetWindowHandle();
            if (hwnd == IntPtr.Zero) return false;
            return GetWindowDisplayAffinity(hwnd, out uint a) && a == WDA_EXCLUDEFROMCAPTURE;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static uint ColorToUint(uint aarrggbb) => ((aarrggbb & 0xFF000000) >> 24) | (aarrggbb & 0x00FF0000) | ((aarrggbb & 0x0000FF00) << 16) | (aarrggbb & 0x000000FF);

        internal unsafe static int GetUtf8(string s, byte* utf8Bytes, int utf8ByteCount)
        {
            fixed (char* chars = s)
            {
                return Encoding.UTF8.GetBytes(chars, s.Length, utf8Bytes, utf8ByteCount);
            }
        }

        internal unsafe static int GetUtf8(string s, int start, int length, byte* utf8Bytes, int utf8ByteCount)
        {
            if (start < 0 || length < 0 || start + length > s.Length) throw new ArgumentOutOfRangeException();
            fixed (char* ptr = s)
            {
                return Encoding.UTF8.GetBytes(ptr + start, length, utf8Bytes, utf8ByteCount);
            }
        }

        internal unsafe static byte* Allocate(int byteCount) => (byte*)(void*)Marshal.AllocHGlobal(byteCount);
        internal unsafe static void Free(byte* ptr) => Marshal.FreeHGlobal((IntPtr)ptr);
        // ---------- Display helpers ----------
        public static class OverlayWindowApi
        {
            private static OverlayWindow? _win;
            internal static void Bind(OverlayWindow win) => _win = win;

            public static void SetFramePacing(bool useVsync, int fpsCap)
            {
                if (_win == null) return;
                _win.SetFramePacing(useVsync, fpsCap);
            }

            public static void Quit()
            {
                // stop render loop; Program/Main and `using` will dispose/exit
                _win?.Close();
            }
        }
        public static void ApplyMonitorSelection(int mon, bool borderless, bool fullscreen)
        {
            // Legacy saved display flags no longer override full-monitor rendering.
            Gui.FullscreenWindow.SelectMonitor(mon);
            ApplyAll();
        }
        public static void CenterOnMonitor(int mon)
        {
            int monCount = Raylib.GetMonitorCount();
            if (monCount <= 0) return;

            mon = Math.Clamp(mon, 0, monCount - 1);
            var pos = Raylib.GetMonitorPosition(mon);
            int mw = Raylib.GetMonitorWidth(mon);
            int mh = Raylib.GetMonitorHeight(mon);
            int ww = Raylib.GetScreenWidth();
            int wh = Raylib.GetScreenHeight();

            int x = (int)pos.X + (mw - ww) / 2;
            int y = (int)pos.Y + (mh - wh) / 2;
            Raylib.SetWindowPosition(x, y);
        }

        public static void RestoreDecorations()
        {
            Raylib.ClearWindowState(ConfigFlags.UndecoratedWindow);
            _undecoratedApplied = false;
            if (Raylib.IsWindowFullscreen()) Raylib.ToggleFullscreen();
        }
        // ---------- Panel helpers ----------
        public static void BeginPanel(string id, string title)
        {
            ImGui.PushFont(Fonts.Bold);
            // title intentionally not printed (cleaner sections)
            ImGui.PopFont();
            ImGui.BeginChild(id, new Vector2(0, 0), ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.None);
            ImGui.Spacing();
        }
        public static void EndPanel()
        {
            ImGui.Spacing();
            ImGui.EndChild();
            ImGui.Dummy(new Vector2(0, 6));
        }
        public static bool BeginFold(string id, string title, bool defaultOpen = false)
        {
            var flags = ImGuiTreeNodeFlags.SpanAvailWidth
                      | (defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : 0);
            bool open = ImGui.CollapsingHeader(title, flags);
            if (open)
            {
                ImGui.PushID(id);
                ImGui.Indent();
                ImGui.Spacing();
            }
            return open;
        }

        public static void EndFold(bool open)
        {
            if (!open) return;
            ImGui.Spacing();
            ImGui.Unindent();
            ImGui.PopID();
        }
        public static class Fonts
        {
            public static ImFontPtr Regular;
            public static ImFontPtr Medium;
            public static ImFontPtr Bold;
        }
        public struct ScreenSettings
        {
            public float W, H;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ScreenSettings(float w, float h) { W = w; H = h; }
        }
        public static string ReadArmaString(ulong ptr)
        {
            if (ptr == 0) return string.Empty;

            // read length (ushort) at offset 0x8
            ushort len = DmaMemory.Read<ushort>(ptr + 0x8);
            if (len == 0 || len > 255) return string.Empty;

            // read chars at offset 0x10
            var bytes = DmaMemory.ReadBytes(ptr + 0x10, (uint)len);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        public static class Win32IconHelper
        {
            private const int ICON_SMALL = 0;
            private const int ICON_BIG = 1;
            private const int WM_SETICON = 0x0080;

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern bool DestroyIcon(IntPtr handle);

            public static unsafe void SetWindowIcons(string iconPath)
            {
                // Load via Raylib first
                Image img = Raylib.LoadImage(iconPath);
                Raylib.SetWindowIcon(img); // sets small icon for titlebar
                Raylib.UnloadImage(img);

                // Load .ico properly for BIG + SMALL icons
                using var icon = new System.Drawing.Icon(iconPath, 256, 256);

                IntPtr hIcon = icon.Handle;
                IntPtr hwnd = (IntPtr)Raylib.GetWindowHandle();

                SendMessage(hwnd, WM_SETICON, ICON_BIG, hIcon);
                SendMessage(hwnd, WM_SETICON, ICON_SMALL, hIcon);
                // Don't call DestroyIcon on .NET Icon.Handle (managed object owns it)
            }
        }
    }
    public static class UiVisibility
    {
        public static bool MenusHidden; // true = hide ALL editor/config/theme/game menus
    }    
}
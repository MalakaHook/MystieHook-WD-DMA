// MystieHook/Input/VK.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MystieHook.Input
{
    /// <summary>
    /// Minimal set of Win32 VK constants + Mouse4/5 we use for keybinds and aimbot.
    /// Keep values as ints to match InputManager API.
    /// </summary>
    public static class VK
    {
        // Mouse / common
        public const int LBUTTON   = 0x01;
        public const int RBUTTON   = 0x02;
        public const int MBUTTON   = 0x04;
        public const int XBUTTON1  = 0x05; // Mouse4
        public const int XBUTTON2  = 0x06; // Mouse5

        // Modifiers
        public const int SHIFT     = 0x10; // VK_SHIFT
        public const int CONTROL   = 0x11; // VK_CONTROL
        public const int MENU      = 0x12; // VK_MENU (Alt)
        public const int CAPITAL   = 0x14; // Caps Lock

        // Function keys
        public const int F1 = 0x70;
        public const int F2 = 0x71;
        public const int F6 = 0x75;
        public const int F9 = 0x78;

        // Letters (common picks)
        public const int VK_Q = 0x51;
        public const int VK_E = 0x45;
        public const int VK_F = 0x46;
        public const int VK_V = 0x56;
        public const int VK_X = 0x58;
        public const int VK_Z = 0x5A;

        // OEM keys
        public const int OEM_TILDE = 0xC0; // ` / ~
    }

    /// <summary>Broker for game/category-scoped keybind actions.</summary>
    public static class Keybinds
    {
        // category -> (actions, handler)
        private static readonly Dictionary<string, (List<string> actions, Action<string> handler)> _map
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Register (or replace) a category's available action names and a handler to execute them.</summary>
        public static void RegisterCategory(string category, IEnumerable<string> actions, Action<string> handler)
        {
            if (string.IsNullOrWhiteSpace(category) || handler is null) return;
            _map[category] = (new List<string>(actions ?? Array.Empty<string>()), handler);
        }

        /// <summary>Get readable action names for a category.</summary>
        public static IReadOnlyList<string> GetActions(string category)
            => _map.TryGetValue(category ?? "Default", out var v) ? v.actions : Array.Empty<string>();

        /// <summary>Dispatch an action by name for a category. Returns true if a handler was called.</summary>
        public static bool Dispatch(string category, string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName)) return false;
            if (_map.TryGetValue(category ?? "Default", out var v))
            {
                v.handler?.Invoke(actionName);
                return true;
            }
            return false;
        }
    }

    public static class VkNames
    {
        private static readonly Dictionary<int, string> Map = new()
        {
            // Control / navigation
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0D] = "Enter",
            [0x13] = "Pause",
            [0x14] = "CapsLock",
            [0x1B] = "Esc",
            [0x20] = "Space",
            [0x21] = "PageUp",
            [0x22] = "PageDown",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2C] = "PrintScreen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",

            // Windows / menu
            [0x5B] = "LWin",
            [0x5C] = "RWin",
            [0x5D] = "Apps",

            // NumPad digits and ops
            [0x60] = "Num0",
            [0x61] = "Num1",
            [0x62] = "Num2",
            [0x63] = "Num3",
            [0x64] = "Num4",
            [0x65] = "Num5",
            [0x66] = "Num6",
            [0x67] = "Num7",
            [0x68] = "Num8",
            [0x69] = "Num9",
            [0x6A] = "Num*",
            [0x6B] = "Num+",
            [0x6D] = "Num-",
            [0x6E] = "Num.",
            [0x6F] = "Num/",

            // Modifiers
            [0xA0] = "LShift",
            [0xA1] = "RShift",
            [0xA2] = "LCtrl",
            [0xA3] = "RCtrl",
            [0xA4] = "LAlt",
            [0xA5] = "RAlt",

            // Media / browser (optional)
            [0xA6] = "BrowserBack",
            [0xA7] = "BrowserForward",
            [0xA8] = "BrowserRefresh",
            [0xA9] = "BrowserStop",
            [0xAA] = "BrowserSearch",
            [0xAB] = "BrowserFavorites",
            [0xAC] = "BrowserHome",
            [0xAD] = "VolumeMute",
            [0xAE] = "VolumeDown",
            [0xAF] = "VolumeUp",
            [0xB0] = "MediaNext",
            [0xB1] = "MediaPrev",
            [0xB2] = "MediaStop",
            [0xB3] = "MediaPlayPause",

            [0x01] = "LButton",
            [0x02] = "RButton",
            [0x04] = "MButton",
            [0x05] = "Mouse4",
            [0x06] = "Mouse5",

            // OEM punctuation (US layout defaults)
            [0xBA] = ";",
            [0xBB] = "=",
            [0xBC] = ",",
            [0xBD] = "-",
            [0xBE] = ".",
            [0xBF] = "/",
            [0xC0] = "`",
            [0xDB] = "[",
            [0xDC] = "\\",
            [0xDD] = "]",
            [0xDE] = "'",
        };

        public static string Name(int vk)
        {
            if (vk <= 0) return null;

            if (Map.TryGetValue(vk, out var s))
                return s;

            // Top-row digits 0..9
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();

            // Letters A..Z
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();

            // Function keys F1..F24 (0x70..0x87)
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);

            // Try OS-provided (localized) name on Windows
            var win = TryWindowsKeyName(vk);
            if (!string.IsNullOrEmpty(win))
                return win;

            // Final fallback
            return $"VK {vk:X2}";
        }

    #if WINDOWS || UNITY_STANDALONE_WIN || WIN32 || WIN64
        const uint MAPVK_VK_TO_VSC = 0x0;

        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetKeyNameText(int lParam, StringBuilder lpString, int nSize);

        static bool IsExtended(int vk) => vk switch
        {
            0x21 or 0x22 or 0x23 or 0x24 or // PgUp/PgDn/End/Home
            0x25 or 0x26 or 0x27 or 0x28 or // Arrows
            0x2D or 0x2E or                 // Ins/Del
            0x5B or 0x5C or 0x5D or         // Win/Apps
            0x6F or                         // Num /
            0x90 or 0x91                    // NumLock/ScrollLock
                => true,
            _ => false
        };

        static string TryWindowsKeyName(int vk)
        {
            try
            {
                uint sc = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
                if (sc == 0) return null;

                int lParam = (int)(sc << 16);
                if (IsExtended(vk)) lParam |= 1 << 24;

                var sb = new StringBuilder(64);
                int len = GetKeyNameText(lParam, sb, sb.Capacity);
                if (len <= 0) return null;

                return sb.ToString();
            }
            catch { return null; }
        }
    #else
        static string TryWindowsKeyName(int vk) => null;
    #endif
    }
}

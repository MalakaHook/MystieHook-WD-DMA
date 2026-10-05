using ImGuiNET;
using Raylib_cs;

namespace MystieHook.Games;

public static partial class GameSelector
{
    private static KeyboardKey LocalKey(int vk) => vk switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => (KeyboardKey)vk,
        >= 0x70 and <= 0x7B => (KeyboardKey)((int)KeyboardKey.F1 + vk - 0x70),
        0x08 => KeyboardKey.Backspace, 0x09 => KeyboardKey.Tab, 0x0D => KeyboardKey.Enter,
        0x1B => KeyboardKey.Escape, 0x20 => KeyboardKey.Space,
        0x21 => KeyboardKey.PageUp, 0x22 => KeyboardKey.PageDown,
        0x23 => KeyboardKey.End, 0x24 => KeyboardKey.Home,
        0x25 => KeyboardKey.Left, 0x26 => KeyboardKey.Up, 0x27 => KeyboardKey.Right, 0x28 => KeyboardKey.Down,
        0x2D => KeyboardKey.Insert, 0x2E => KeyboardKey.Delete,
        _ => KeyboardKey.Null
    };

    private static bool LocalCtrl => Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
    private static bool LocalShift => Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
    private static bool LocalAlt => Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);

    private static void ProcessLocalKeybinds()
    {
        if (_bindEditorOpen || _wantsRebind || ImGui.GetIO().WantTextInput || ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId)) return;
        foreach (var bind in _activeProfile.Binds)
        {
            var key = LocalKey(bind.Vk);
            if (key == KeyboardKey.Null || !bind.MatchesModifiers(LocalCtrl, LocalShift, LocalAlt)) continue;
            bool fire = bind.Mode switch
            {
                KeybindMode.OnPress => Raylib.IsKeyPressed(key),
                KeybindMode.OnRelease => Raylib.IsKeyReleased(key),
                _ => Raylib.IsKeyDown(key)
            };
            if (fire) RunActionString(bind.Action);
        }
        // Recovery if the user deletes every menu binding.
        if (!_activeProfile.Binds.Any(b => b.Action == "ShowMenu") && Raylib.IsKeyPressed(KeyboardKey.F10)) ToggleMenu();
    }
}

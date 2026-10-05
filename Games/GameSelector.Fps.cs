using System.Numerics;
using ImGuiNET;

namespace MystieHook.Games;

public static partial class GameSelector
{
    private static bool _showFps;

    private static void DrawFps()
    {
        string text = $"{ImGui.GetIO().Framerate:0} FPS";
        var viewport = ImGui.GetMainViewport();
        var size = ImGui.CalcTextSize(text);
        var position = viewport.Pos + new Vector2((viewport.Size.X - size.X) / 2, 10);
        var draw = ImGui.GetForegroundDrawList();
        draw.AddText(position + Vector2.One, 0xFF000000, text);
        draw.AddText(position, 0xFFFFFFFF, text);
    }
}

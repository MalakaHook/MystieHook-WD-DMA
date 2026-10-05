using MystieHook.Gui;
using ImGuiNET;
using System.Numerics;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogGame
{
    private static void DrawAimCurvePreview(WardogAimCurve curve, float duration)
    {
        var size = new Vector2(Math.Max(1, Math.Min(440, ImGui.GetContentRegionAvail().X)), 150);
        ImGui.InvisibleButton("Aim curve preview", size);
        var origin = ImGui.GetItemRectMin();
        var min = origin + new Vector2(12, 12);
        var max = origin + size - new Vector2(12, 12);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(ImGuiCol.FrameBg), 6);
        uint grid = ImGui.GetColorU32(ImGuiCol.Border);
        uint accent = ImGui.GetColorU32(ImGuiCol.CheckMark);
        for (int i = 0; i <= 4; i++)
        {
            float t = i / 4f;
            draw.AddLine(new(min.X + (max.X - min.X) * t, min.Y), new(min.X + (max.X - min.X) * t, max.Y), grid);
            draw.AddLine(new(min.X, min.Y + (max.Y - min.Y) * t), new(max.X, min.Y + (max.Y - min.Y) * t), grid);
        }
        Vector2 Point(double t) => new(min.X + (max.X - min.X) * (float)t,
            max.Y - (max.Y - min.Y) * (float)WardogAimCurves.Progress(curve, t));
        var previous = Point(0);
        for (int i = 1; i <= 96; i++)
        {
            var next = Point(i / 96.0);
            draw.AddLine(previous, next, accent, 2);
            previous = next;
        }
        draw.AddCircleFilled(Point(ImGui.GetTime() / 2 % 1), 4, accent);
        if (ImGui.IsItemHovered())
        {
            double t = Math.Clamp((ImGui.GetIO().MousePos.X - min.X) / Math.Max(1, max.X - min.X), 0, 1);
            SafeText.Tooltip($"Time: {duration * t:0} ms\nMovement progress: {100 * WardogAimCurves.Progress(curve, t):0.0}%");
        }
        SafeText.Disabled($"Time: 0 - {duration:0} ms | Movement progress: 0 - 100%");
        SafeText.Wrapped(curve == WardogAimCurve.Exponential
            ? "Current response: quick start, then gradual settling; about 95% progress at the response time."
            : curve == WardogAimCurve.Linear ? "Linear: constant initial movement rate."
            : curve == WardogAimCurve.EaseIn ? "Ease in: starts slowly, then accelerates."
            : curve == WardogAimCurve.EaseOut ? "Ease out: starts quickly, then slows down."
            : "S-curve: gradual acceleration and deceleration.");
        SafeText.Disabled("Stationary-target preview before speed cap, dead zone and variability.");
    }
}

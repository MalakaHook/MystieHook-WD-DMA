using System.Numerics;
using ImGuiNET;
using MystieHook.Services;

namespace MystieHook.Gui;

public static class PerformanceDiagnosticsView
{
    public static void Draw(SessionDiagnostics history, ref int window)
    {
        ImGui.SeparatorText("Performance history");
        ImGui.SetNextItemWidth(150);
        ImGui.Combo("History", ref window, "30 seconds\060 seconds\0");
        double seconds = window == 0 ? 30 : 60;
        SafeText.Disabled("Records with the menu hidden. Peak times / lowest FPS per 100 ms; hover a graph for details.");
        SafeText.Disabled("Red marks stale/context-mismatched frames; amber marks missing data or attachment. Gaps = no sample.");
        if (history.Count == 0) { ImGui.TextUnformatted("Waiting for the first observation..."); return; }
        Plot(history, seconds, 0, "Reader time", "ms", 20, 0);
        Plot(history, seconds, 1, "Camera age", "ms", 120, 100);
        Plot(history, seconds, 2, "Player age", "ms", 300, 250);
        Plot(history, seconds, 3, "Overlay FPS", "FPS", 144, 0);
    }

    private static double? Metric(PerformancePoint p, int kind) => kind switch
    { 0 => p.ReaderMs, 1 => p.CameraAgeMs, 2 => p.PlayerAgeMs, _ => p.MinimumFps };

    private static void Plot(SessionDiagnostics history, double seconds, int kind, string label, string unit, double scale, double limit)
    {
        double now = history.Latest.TimeSeconds, left = now - seconds;
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(Math.Max(80, ImGui.GetContentRegionAvail().X), 84);
        ImGui.InvisibleButton(label, size);
        if (!ImGui.IsItemVisible()) return;
        for (int i = 0; i < history.Count; i++)
        {
            var p = history.Point(i);
            if (p.TimeSeconds >= left && Metric(p, kind) is double value) scale = Math.Max(scale, value * 1.1);
        }
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ImGuiCol.FrameBg), 4);
        var top = pos + new Vector2(7, 23); var bottom = pos + size - new Vector2(7, 5);
        float width = Math.Max(1, bottom.X - top.X), height = Math.Max(1, bottom.Y - top.Y);
        float X(double time) => top.X + (float)((time - left) / seconds) * width;
        float Y(double value) => bottom.Y - (float)Math.Clamp(value / scale, 0, 1) * height;
        uint accent = ImGui.GetColorU32(ImGuiCol.CheckMark);
        draw.PushClipRect(top, bottom, true);
        if (limit > 0) draw.AddLine(new(top.X, Y(limit)), new(bottom.X, Y(limit)), 0x88FFFFFF);
        Vector2? previous = null; double previousTime = 0;
        for (int i = 0; i < history.Count; i++)
        {
            var p = history.Point(i);
            if (p.TimeSeconds < left) continue;
            if (p.UnavailableFrames > 0)
            {
                bool stale = (p.Issues & (DataIssue.PlayersStale | DataIssue.CameraStale | DataIssue.ContextMismatch)) != 0;
                draw.AddRectFilled(new(X(p.TimeSeconds - .1), top.Y), new(Math.Max(X(p.TimeSeconds), X(p.TimeSeconds - .1) + 1), bottom.Y), stale ? 0x554444FFu : 0x5533BBFFu);
            }
            var value = Metric(p, kind);
            if (value is null) { previous = null; continue; }
            var point = new Vector2(X(p.TimeSeconds), Y(value.Value));
            if (previous is Vector2 from && p.TimeSeconds - previousTime <= .25) draw.AddLine(from, point, accent, 1.5f);
            else draw.AddCircleFilled(point, 1.5f, accent);
            previous = point; previousTime = p.TimeSeconds;
        }
        draw.PopClipRect();
        var latest = Metric(history.Point(history.Count - 1), kind);
        draw.AddText(pos + new Vector2(7, 3), ImGui.GetColorU32(ImGuiCol.Text), $"{label}: {(latest is double v ? v.ToString("0.0") : "unavailable")} {unit}  | scale 0-{scale:0}{(limit > 0 ? $" | limit {limit:0}" : "")}");
        if (ImGui.IsItemHovered())
        {
            double time = left + Math.Clamp((ImGui.GetMousePos().X - top.X) / width, 0, 1) * seconds;
            int closest = -1; double distance = .2;
            for (int i = 0; i < history.Count; i++)
            { double d = Math.Abs(history.Point(i).TimeSeconds - time); if (d < distance) { closest = i; distance = d; } }
            if (closest < 0) SafeText.Tooltip("No observation here (startup, pause or UI stall).");
            else
            {
                var p = history.Point(closest);
                SafeText.Tooltip($"{now - p.TimeSeconds:0.0}s ago | {p.UnavailableFrames}/{p.Frames} frames lacked matching fresh data\nIssues: {p.Issues}\nReader: {p.ReaderMs:0.0} ms | DMA: {p.DmaMs:0.0} ms | wait: {p.WaitMs:0.0} ms\nCamera age: {p.CameraAgeMs:0.0} ms | player age: {p.PlayerAgeMs:0.0} ms\nLowest FPS: {p.MinimumFps:0.0} | batch: {p.Batch} | direct: {p.Direct}");
            }
        }
    }
}

using ImGuiNET;
using System.Numerics;

namespace MystieHook.Games.Wardog;

public static class WardogVehicleOverlay
{
    public static void Draw(WardogReader.VehicleFrame vehicles, WardogReader.Frame context, WardogVehicleSettings settings)
    {
        if (!settings.ShowVehicles || !vehicles.IsFresh || !context.IsFresh || vehicles.World != context.WorldAddress) return;
        var viewport = ImGui.GetMainViewport();
        var draw = ImGui.GetForegroundDrawList();
        foreach (var vehicle in vehicles.Vehicles)
        {
            if(!settings.ShowTeammateVehicles && vehicle.TeamRelation==WardogReader.Relation.Friendly)continue;
            double distance = vehicle.Position.Distance(context.LocalPosition) / 100;
            if (distance > settings.MaximumDistance || !WardogReader.Project(context.Camera, vehicle.Position,
                viewport.Size.X, viewport.Size.Y, out var point)) continue;
            if (point.X < 0 || point.Y < 0 || point.X > viewport.Size.X || point.Y > viewport.Size.Y) continue;
            string text = $"{vehicle.DisplayLabel} [{distance:0} m]";
            var position = viewport.Pos + point - new Vector2(ImGui.CalcTextSize(text).X / 2, 0);
            draw.AddText(position + Vector2.One, 0xFF000000, text);
            draw.AddText(position, 0xFFFFD080, text);
        }
    }
}

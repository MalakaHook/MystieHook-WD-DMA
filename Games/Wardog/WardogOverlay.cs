using ImGuiNET;
using System.Numerics;

namespace MystieHook.Games.Wardog;

// Draws completed local snapshots only; rendering never reads the device.
public static class WardogOverlay
{
    public static void Draw(WardogReader.Frame frame, WardogVisualSettings visuals)
    {
        if (!frame.IsFresh) return;
        var viewport = ImGui.GetMainViewport();
        DrawPlayers(frame, visuals, ImGui.GetForegroundDrawList(), viewport.Pos, viewport.Size);
    }
    internal static void DrawPreview(WardogReader.Frame sample, WardogVisualSettings visuals, ImDrawListPtr draw, Vector2 origin, Vector2 size)
        => DrawPlayers(sample, visuals, draw, origin, size);
    private static void DrawPlayers(WardogReader.Frame frame, WardogVisualSettings visuals, ImDrawListPtr draw, Vector2 origin, Vector2 size)
    {
        foreach (var player in frame.Players)
        {
            if (!visuals.Allows(player.Position, frame.LocalPosition, player.TeamRelation, player.Dead, player.Downed)) continue;
            if (player.Dead)
            {
                if (WardogReader.Project(frame.Camera, player.Position, size.X, size.Y, out var deadPosition))
                {
                    var anchor = deadPosition + origin;
                    Text(draw, player.Name, anchor - new Vector2(0, visuals.NameTextSize + 2), visuals.NameTextSize);
                    Text(draw, "Dead", anchor, visuals.NameTextSize);
                }
                continue;
            }
            if(player.Downed)
            {
                if(WardogReader.Project(frame.Camera,player.Position,size.X,size.Y,out var downedPosition))
                    Text(draw,"Downed",downedPosition+origin,visuals.NameTextSize);
                continue;
            }
            uint teamColor = player.TeamRelation == WardogReader.Relation.Friendly ? 0xFF70E070u :
                player.TeamRelation == WardogReader.Relation.Enemy ? 0xFF7070FFu : 0xFF00FFFFu;
            var pose = player.Pose;
            if (visuals.ShowBones && pose.Points.Length == 22 && pose.Valid.Length == 22)
                foreach (var (a, b) in WardogReader.BoneLinks)
                {
                    if (!pose.Valid[a] || !pose.Valid[b]) continue;
                    if (WardogReader.Project(frame.Camera, pose.Points[a], size.X, size.Y, out var pa)
                        && WardogReader.Project(frame.Camera, pose.Points[b], size.X, size.Y, out var pb))
                        draw.AddLine(pa + origin, pb + origin, visuals.BoneColor ?? teamColor, visuals.BoneThickness);
                }
            if (visuals.ShowHeadDot && player.HeadPose.HasHead
                && WardogReader.Project(frame.Camera, player.HeadPose.Points[0], size.X, size.Y, out var head))
                draw.AddCircleFilled(head + origin, visuals.HeadDotSize, visuals.HeadDotColor ?? teamColor);

            if (!WardogDrawing.Bounds(player, frame.Camera, size, out var projected)) continue;
            var box = new WardogScreenBounds(projected.Min + origin, projected.Max + origin);
            if (visuals.ShowBoxes) DrawBox(draw, box, visuals.BoxStyle, visuals.BoxColor ?? teamColor);
            bool health = visuals.ShowHealth && player.Health >= 0;
            if (health) DrawHealth(draw, box, player.Health, visuals.HealthPosition);
            float above = box.Min.Y - 4 - (health && visuals.HealthPosition == WardogHealthPosition.Top ? 8 : 0);
            float below = box.Max.Y + 4 + (health && visuals.HealthPosition == WardogHealthPosition.Bottom ? 8 : 0);
            // Stack upwards so faction is below the name and clear of the box/bar.
            if (visuals.ShowFaction)
            {
                above -= visuals.FactionTextSize;
                Text(draw, player.Faction, new(box.CenterX, above), visuals.FactionTextSize);
                above -= 2;
            }
            if (visuals.ShowName)
                Text(draw, player.Name, new(box.CenterX, above - visuals.NameTextSize), visuals.NameTextSize);
            if (visuals.ShowCurrentItem && !string.IsNullOrWhiteSpace(player.CurrentItem))
            {
                Text(draw, player.CurrentItem, new(box.CenterX, below));
                below += ImGui.GetTextLineHeight() + 2;
            }
            if (visuals.ShowDistances)
                Text(draw, $"{player.Position.Distance(frame.LocalPosition) / 100:0} m", new(box.CenterX, below), visuals.DistanceTextSize);
        }
    }
    private static void Text(ImDrawListPtr draw, string value, Vector2 position, float size = 14)
    {
        position.X -= ImGui.CalcTextSize(value).X * (size / ImGui.GetFontSize()) / 2;
        draw.AddText(ImGui.GetFont(), size, position + Vector2.One, 0xFF000000, value);
        draw.AddText(ImGui.GetFont(), size, position, 0xFFFFFFFF, value);
    }
    private static void DrawBox(ImDrawListPtr draw, WardogScreenBounds box, WardogBoxStyle style, uint color)
    {
        if (style == WardogBoxStyle.Normal)
        {
            draw.AddRect(box.Min, box.Max, 0xFF000000, 0, ImDrawFlags.None, 3);
            draw.AddRect(box.Min, box.Max, color);
            return;
        }
        float x = box.Width * .25f, y = box.Height * .25f;
        foreach (var corner in new[] { box.Min, new Vector2(box.Max.X, box.Min.Y), box.Max, new Vector2(box.Min.X, box.Max.Y) })
        {
            var endX = corner + new Vector2(corner.X == box.Min.X ? x : -x, 0);
            var endY = corner + new Vector2(0, corner.Y == box.Min.Y ? y : -y);
            draw.AddLine(corner, endX, 0xFF000000, 3); draw.AddLine(corner, endY, 0xFF000000, 3);
            draw.AddLine(corner, endX, color); draw.AddLine(corner, endY, color);
        }
    }
    private static void DrawHealth(ImDrawListPtr draw, WardogScreenBounds box, int percent, WardogHealthPosition side)
    {
        Vector2 min, max;
        bool vertical = side is WardogHealthPosition.Left or WardogHealthPosition.Right;
        switch (side)
        {
            case WardogHealthPosition.Right: min = new(box.Max.X + 4, box.Min.Y); max = new(box.Max.X + 8, box.Max.Y); break;
            case WardogHealthPosition.Top: min = new(box.Min.X, box.Min.Y - 8); max = new(box.Max.X, box.Min.Y - 4); break;
            case WardogHealthPosition.Bottom: min = new(box.Min.X, box.Max.Y + 4); max = new(box.Max.X, box.Max.Y + 8); break;
            default: min = new(box.Min.X - 8, box.Min.Y); max = new(box.Min.X - 4, box.Max.Y); break;
        }
        draw.AddRectFilled(min - Vector2.One, max + Vector2.One, 0xFF000000);
        draw.AddRectFilled(min, max, 0xFF303030);
        float fraction = Math.Clamp(percent / 100f, 0, 1);
        if (fraction <= 0) return;
        var filledMin = min; var filledMax = max;
        if (vertical) filledMin.Y = max.Y - (max.Y - min.Y) * fraction;
        else filledMax.X = min.X + (max.X - min.X) * fraction;
        uint color = percent <= 25 ? 0xFF5050FFu : percent <= 60 ? 0xFF50DFFFu : 0xFF60DF60u;
        draw.AddRectFilled(filledMin, filledMax, color);
    }
}

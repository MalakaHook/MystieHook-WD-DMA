using System.Numerics;

namespace MystieHook.Games.Wardog;

public readonly record struct WardogScreenBounds(Vector2 Min, Vector2 Max)
{
    public float Width => Max.X - Min.X;
    public float Height => Max.Y - Min.Y;
    public float CenterX => (Min.X + Max.X) / 2;
}

// Pure projection/layout shared by every player annotation.
public static class WardogDrawing
{
    public static bool Bounds(WardogReader.Player player, WardogReader.Camera camera,
        Vector2 viewport, out WardogScreenBounds bounds)
    {
        bounds = default;
        var upper = player.Position;
        var lower = player.Position;
        upper.Z += player.Downed || player.Dead ? 38 : 90;
        lower.Z -= player.Downed || player.Dead ? 38 : 90;
        if (player.HeadPose.HasHead) upper = player.HeadPose.Points[0];
        // Bone pivots are inside the skull/ankle, not the extremities of the model.
        upper.Z += 18;
        lower.Z -= 12;
        if (!WardogReader.Project(camera, upper, viewport.X, viewport.Y, out var top)
            || !WardogReader.Project(camera, lower, viewport.X, viewport.Y, out var bottom)) return false;
        float minY = Math.Min(top.Y, bottom.Y), maxY = Math.Max(top.Y, bottom.Y);
        float centerX = (top.X + bottom.X) / 2;
        float halfWidth = (maxY - minY) * .275f;
        float minX = centerX - halfWidth, maxX = centerX + halfWidth;
        // Include posed limbs as well, so a prone or moving player isn't clipped.
        for (int i = 0; i < player.Pose.Points.Length && i < player.Pose.Valid.Length; i++)
        {
            if (!player.Pose.Valid[i]) continue;
            var point = player.Pose.Points[i];
            if (i is 8 or 16) point.Z -= 12;
            if (!WardogReader.Project(camera, point, viewport.X, viewport.Y, out var screen)) continue;
            minX = Math.Min(minX, screen.X); maxX = Math.Max(maxX, screen.X);
            minY = Math.Min(minY, screen.Y); maxY = Math.Max(maxY, screen.Y);
        }
        float pad = Math.Max(2, (maxY - minY) * .025f);
        bounds = new(new(minX - pad, minY - pad), new(maxX + pad, maxY + pad));
        return bounds.Height > 1 && bounds.Width > 1 && float.IsFinite(bounds.Height) && float.IsFinite(bounds.Width);
    }
}

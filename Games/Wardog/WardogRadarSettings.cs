using System.Numerics;
namespace MystieHook.Games.Wardog;
public sealed record WardogRadarSettings(bool Enabled=false, bool HeadingUp=true, bool Names=false,
    bool Vehicles=true, float Range=500, float Size=300, float X=20, float Y=80)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool WebSharing { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool LocalMap { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool DebugNames { get; init; }
    public bool Players { get; init; } = true;
    public bool Teammates { get; init; } = true;
    public bool AutoMap { get; init; } = true;
    public int Map { get; init; }
    public float Zoom { get; init; } = 16;
    public WardogRadarSettings ApplyDetectedMap(WardogReader.Frame frame) => AutoMap && WardogArtillerySettings.HasDetectedMap(frame)
        ? this with {Map=frame.DetectedMap} : this;
    public float CaptureRange => (float)Math.Min(WardogReadRange.MaximumMetres,WardogArtilleryData.ImageBounds(WardogArtilleryData.Maps[Map]).Size/100/Zoom);
    public bool Valid => float.IsFinite(Range) && Range is >=10 and <=10000
        && Map is >=0 and <=2 && float.IsFinite(Zoom) && Zoom is >=1 and <=128
        && float.IsFinite(Size) && Size is >=180 and <=700
        && float.IsFinite(X) && X is >=0 and <=16000 && float.IsFinite(Y) && Y is >=0 and <=16000;
    public static Vector2 Rotate(double dx, double dy, double yaw, bool heading)
    {
        if (!heading) return new((float)dy,(float)-dx);
        double a=yaw*Math.PI/180, c=Math.Cos(a), s=Math.Sin(a);
        return new((float)(dy*c-dx*s),(float)-(dx*c+dy*s));
    }
}

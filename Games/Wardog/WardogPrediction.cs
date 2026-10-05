using System.Diagnostics;
using System.Numerics;
using Vec = MystieHook.Games.Wardog.WardogReader.Vec;
namespace MystieHook.Games.Wardog;

// Port of the supplied prediction.h. Motion stays on the existing player worker.
public sealed class WardogPrediction
{
    private sealed record Track(Vec Root, long Stamp);
    private Dictionary<(ulong, ulong, ulong, ulong), Track> _tracks = new();
    private (ulong, ulong, ulong) _context;
    public WardogReader.Frame Observe(WardogReader.Frame frame, bool enabled)
    {
        var context = (frame.WorldAddress, frame.LocalController, frame.LocalPawn);
        if (!enabled || !frame.IsFresh || context != _context || _tracks.Count > 1024) _tracks.Clear();
        _context = context;
        if (!enabled || !frame.IsFresh) return frame.Players.Any(p=>p.PredictionVelocity.HasValue)
            ? frame with { Players=frame.Players.Select(p=>p with {PredictionVelocity=null}).ToArray() } : frame;
        var next = new Dictionary<(ulong, ulong, ulong, ulong), Track>();
        var players = new WardogReader.Player[frame.Players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            var p = frame.Players[i]; players[i] = p with { PredictionVelocity = null };
            if (i >= 1024 || p.Dead || p.Health <= 0 || !p.Position.Valid() || p.SourceState == 0 || p.SourceMesh == 0) continue;
            var key = (p.Address, p.SourceState, p.SourceMesh, p.SourceAsset);
            if (_tracks.TryGetValue(key, out var old) && frame.SampleStarted > old.Stamp
                && EstimateLeadVelocity(old.Root, p.Position, Stopwatch.GetElapsedTime(old.Stamp, frame.SampleStarted).TotalSeconds, out var velocity))
                players[i] = players[i] with { PredictionVelocity = velocity };
            next[key] = new(p.Position, frame.SampleStarted);
        }
        _tracks = next;
        return frame with { Players = players };
    }
    public static bool EstimateLeadVelocity(Vec previous, Vec current, double dt, out Vec velocity)
    {
        velocity = default;
        if (!double.IsFinite(dt) || dt <= 0 || dt > .2 || !previous.Valid() || !current.Valid()) return false;
        velocity = new() { X = (current.X-previous.X)/dt, Y = (current.Y-previous.Y)/dt, Z = (current.Z-previous.Z)/dt };
        double v2 = velocity.X*velocity.X+velocity.Y*velocity.Y+velocity.Z*velocity.Z;
        return double.IsFinite(v2) && v2 <= 25000000;
    }
    public static bool PredictHeadAim(Vec origin, Vec head, Vec velocity, double speed, double gravity, out Vec aim, out double flightSeconds)
    {
        aim = default; flightSeconds = 0;
        if (!origin.Valid() || !head.Valid() || !velocity.Valid() || !double.IsFinite(speed) || speed is <2000 or >200000
            || !double.IsFinite(gravity) || gravity is <0 or >5000) return false;
        Vec Displacement(double t) => new() { X=head.X-origin.X+velocity.X*t, Y=head.Y-origin.Y+velocity.Y*t,
            Z=head.Z-origin.Z+velocity.Z*t+.5*gravity*t*t };
        double Error(double t) { var d=Displacement(t); return d.X*d.X+d.Y*d.Y+d.Z*d.Z-speed*speed*t*t; }
        if (Error(0) < 1e-8) return false;
        double lo=0, hi=0; bool found=false;
        for(int i=1;i<=256;i++) { hi=5.0*i/256; if(Error(hi)<=0){found=true;break;} lo=hi; }
        if(!found)return false;
        for(int i=0;i<48;i++) { double mid=(lo+hi)*.5; if(Error(mid)>0)lo=mid;else hi=mid; }
        flightSeconds=(lo+hi)*.5;
        var d=Displacement(flightSeconds);
        aim=new(){X=origin.X+d.X,Y=origin.Y+d.Y,Z=origin.Z+d.Z};
        return aim.Valid();
    }
    public readonly record struct Dot(Vector2 Point, double DistanceMetres, double FlightSeconds);
    public static Dot? ForPlayer(WardogReader.Frame frame, WardogReader.Player player, WardogAimSettings settings, int width, int height)
    {
        if (!frame.IsFresh || !frame.HasPredictionSpeed || frame.ReadMilliseconds > 200
            || Stopwatch.GetElapsedTime(frame.SampleStarted).TotalSeconds is <0 or >.2
            || player.Dead || player.Health <= 0 || player.TeamRelation != WardogReader.Relation.Enemy
            || player.Position.Distance(frame.LocalPosition)/100 > settings.Distance
            || player.PredictionVelocity is not Vec velocity) return null;
        if(player.PoseSampleStarted==0 || Stopwatch.GetElapsedTime(player.PoseSampleStarted).TotalMilliseconds is <0 or >80)return null;
        if(WardogAimSettings.SelectPoint(frame with {Players=[player]},settings with {Enabled=true,TeamCheck=true},width,height,out _,out var bone)==null)return null;
        if (!PredictHeadAim(frame.Camera.Location,bone,velocity,frame.LocalBulletSpeed,settings.BulletGravityCm,out var lead,out double flight)
            || !WardogReader.Project(frame.Camera,lead,width,height,out var point)
            || point.X<0||point.Y<0||point.X>width||point.Y>height)return null;
        return new(point,player.Position.Distance(frame.LocalPosition)/100,flight);
    }
    public static Vector2? Marker(WardogReader.Frame frame,WardogAimSettings settings,int width,int height)
        => settings.ShowPrediction ? Select(frame,settings,width,height,out _) : null;
    public static Vector2? Select(WardogReader.Frame frame,WardogAimSettings settings,int width,int height,out ulong address)
    {
        address=0;
        if(WardogAimSettings.SelectPoint(frame,settings with {Enabled=true,TeamCheck=true},width,height,out var player,out _) == null)return null;
        var dot=ForPlayer(frame,player,settings,width,height);
        if(dot is not Dot value)return null;
        address=player.Address;return value.Point;
    }
}

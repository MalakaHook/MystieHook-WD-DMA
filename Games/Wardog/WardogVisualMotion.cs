using System.Diagnostics;
using F = MystieHook.Games.Wardog.WardogReader.Frame;
using P = MystieHook.Games.Wardog.WardogReader.Player;
using V = MystieHook.Games.Wardog.WardogReader.Vec;
using Pose = MystieHook.Games.Wardog.WardogReader.SkeletonPose;
namespace MystieHook.Games.Wardog;

// Render-only reference history; original acquisition timestamps remain untouched.
public sealed class WardogVisualMotion
{
    private readonly object _sync = new();
    private F[] _samples = [];
    private static double Age(long from,long to) => (to-from)/(double)Stopwatch.Frequency;
    public void Clear() { lock(_sync) _samples=[]; }
    // Interpolate world-space actors only. View rotation/translation/FOV must come
    // from the newest independent camera, with original camera/player age gates.
    public F SampleForCamera(long now, WardogReader.CameraFrame camera, bool smooth=true) =>
        WardogReader.WithCamera(Sample(now,smooth),camera);
    public void Push(F frame)
    {
        lock(_sync)
        {
            if(!frame.Valid) { _samples=[];return; }
            if(_samples.Length>0)
            {
                var last=_samples[^1];
                if(frame.WorldAddress!=last.WorldAddress || frame.LocalController!=last.LocalController ||
                    frame.LocalPawn!=last.LocalPawn || Age(last.CompletedAt,frame.CompletedAt) is <=0 or >.15) _samples=[];
            }
            _samples=_samples.Append(frame).Where(f=>Age(f.CompletedAt,frame.CompletedAt)<=.25).TakeLast(12).ToArray();
        }
    }
    public F Sample(long now,bool smooth=true,double delay=.025)
    {
        F[] samples;lock(_sync) samples=_samples;
        if(samples.Length==0)return F.Empty("No player frame");
        var latest=samples[^1];double retention=smooth?.15:.5;
        if(Age(latest.CompletedAt,now)<0 || Age(latest.CompletedAt,now)>retention)return F.Empty("Player display expired");
        var result=latest with {DisplayUntil=latest.CompletedAt+(long)(retention*Stopwatch.Frequency)};
        if(!smooth || delay<=0 || samples.Length<2)return result;
        long target=now-(long)(Math.Clamp(delay,0,.06)*Stopwatch.Frequency);
        var before=samples[0];var after=latest;
        foreach(var sample in samples)
        {
            if(sample.CompletedAt<=target)before=sample;
            if(sample.CompletedAt>=target){after=sample;break;}
        }
        double dt=Age(before.CompletedAt,after.CompletedAt);
        if(dt is <=0 or >.1 || before.Camera.Location.Distance(after.Camera.Location)>1000 ||
            Math.Abs(AngleDelta(before.Camera.Rotation.Y,after.Camera.Rotation.Y))>45 ||
            Math.Abs(after.Camera.Rotation.X-before.Camera.Rotation.X)>45 ||
            Math.Abs(after.Camera.Fov-before.Camera.Fov)>20)return result;
        double t=Math.Clamp(Age(before.CompletedAt,target)/dt,0,1);
        var first=before.Players.ToDictionary(p=>p.Address);var second=after.Players.ToDictionary(p=>p.Address);
        P Blend(P player)
        {
            if(!first.TryGetValue(player.Address,out var a)||!second.TryGetValue(player.Address,out var b)||
                !Same(a,player)||!Same(b,player)||a.Position.Distance(b.Position)>1000)return player;
            var root=Lerp(a.Position,b.Position,t);
            var shift=new V{X=root.X-player.Position.X,Y=root.Y-player.Position.Y,Z=root.Z-player.Position.Z};
            Pose BlendPose(Pose current,Pose left,Pose right)
            {
                var points=(V[])current.Points.Clone();var valid=(bool[])current.Valid.Clone();
                for(int i=0;i<points.Length && i<valid.Length;i++)
                {
                    if(i<left.Points.Length&&i<right.Points.Length&&i<left.Valid.Length&&i<right.Valid.Length&&
                        left.Valid[i]&&right.Valid[i]&&left.Points[i].Distance(right.Points[i])<1200)
                    {points[i]=Lerp(left.Points[i],right.Points[i],t);valid[i]=true;}
                    else if(valid[i])points[i]=new V{X=points[i].X+shift.X,Y=points[i].Y+shift.Y,Z=points[i].Z+shift.Z};
                }
                return new(points,valid);
            }
            return player with {Position=root,Pose=BlendPose(player.Pose,a.Pose,b.Pose),HeadPose=BlendPose(player.HeadPose,a.HeadPose,b.HeadPose)};
        }
        return result with
        {
            Players=latest.Players.Select(Blend).ToArray()
        };
    }
    private static bool Same(P a,P b)=>a.Address==b.Address&&a.SourceState==b.SourceState&&a.SourceMesh==b.SourceMesh&&
        a.SourceAsset==b.SourceAsset&&a.Dead==b.Dead&&a.Downed==b.Downed;
    private static double AngleDelta(double a,double b)=>Math.IEEERemainder(b-a,360);
    private static V Lerp(V a,V b,double t)=>new(){X=a.X+(b.X-a.X)*t,Y=a.Y+(b.Y-a.Y)*t,Z=a.Z+(b.Z-a.Z)*t};
}


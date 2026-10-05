using System.Numerics;
namespace MystieHook.Games.Wardog;
public enum WardogAimTarget { ClosestToCrosshair, ClosestToMe }
public sealed record WardogAimSettings
{
 // Compatibility members only; retired smoothing is neither loaded nor saved or used for movement.
 [System.Text.Json.Serialization.JsonIgnore] public bool SmoothXEnabled {get;init;}
 [System.Text.Json.Serialization.JsonIgnore] public bool SmoothYEnabled {get;init;}
 public bool UseCalibration {get;init;} public WardogAimProfile Calibration {get;init;}
 public bool Enabled {get;init;}
 public bool ShowPrediction {get;init;}
 public bool UsePrediction {get;init;}
 public float BulletGravityCm {get;init;}=980;
 public float PredictionDotRadius {get;init;}=2;
 public bool ShowPredictionDetails {get;init;}
 public uint HeadPredictionColor {get;init;}=0xFF00CCFF;
 public WardogAimTarget AimTarget {get;init;}
 [System.Text.Json.Serialization.JsonIgnore] public bool NeedsPrediction => ShowPrediction || (Enabled && UsePrediction);
 [System.Text.Json.Serialization.JsonIgnore] public float SmoothX {get;init;}=10;
 [System.Text.Json.Serialization.JsonIgnore] public float SmoothY {get;init;}=20;
 public float Fov {get;init;}=80; public bool ShowFov {get;init;}=true;
 public int Key {get;init;}=2; public int Bone {get;init;}=1;
 public bool TargetLine {get;init;} public uint LineColor {get;init;}=0xFF5858ED;
 public bool TeamCheck {get;init;}=true;
 public float Distance {get;init;}=100;
 public bool Valid => float.IsFinite(BulletGravityCm)&&BulletGravityCm is >=0 and <=5000
 && float.IsFinite(PredictionDotRadius)&&PredictionDotRadius is >=1 and <=20
 && Enum.IsDefined(AimTarget) && (!UseCalibration || Calibration is { Valid:true }) && (Calibration is null || Calibration.Valid)
 && float.IsFinite(Fov)&&Fov is >=1 and <=600 && float.IsFinite(Distance)&&Distance is >=1 and <=10000 && Key is >=1 and <=254 && Bone is >=0 and <=4;
 public bool CalibrationReady(int width,int height,float fov) => UseCalibration && Calibration is {Valid:true} p
  && p.Width==width && p.Height==height && (p.LocalMouse || (float.IsFinite(fov)&&fov is >1 and <179));
 public static Vector2? Select(WardogReader.Frame frame,WardogAimSettings s,int width,int height) => Select(frame,s,width,height,out _);
 public static Vector2? Select(WardogReader.Frame frame,WardogAimSettings s,int width,int height,out ulong targetAddress)
 {
  targetAddress=0;
  if(!s.Enabled)return null;
  if(s.UsePrediction)return WardogPrediction.Select(frame,s,width,height,out targetAddress);
  var result=SelectPoint(frame,s,width,height,out var player,out _);
  targetAddress=player?.Address ?? 0;
  return result;
 }
 // Select the actual bone first; both movement and marker use this acquisition policy.
 public static Vector2? SelectPoint(WardogReader.Frame frame,WardogAimSettings s,int width,int height,out WardogReader.Player player,out WardogReader.Vec worldPoint)
 {
  player=null;worldPoint=default;
  if(!s.Enabled||!Enum.IsDefined(s.AimTarget)||!frame.IsFresh||width<=0||height<=0)return null;
  float best=s.Fov*s.Fov;Vector2? result=null;var center=new Vector2(width/2f,height/2f);
  double bestRange=double.PositiveInfinity;
  foreach(var p in frame.Players)
  {
   double range=p.Position.Distance(frame.LocalPosition)/100;
   if(p.Dead||p.Health<=0||!double.IsFinite(range)||range>s.Distance || (s.TeamCheck&&p.TeamRelation!=WardogReader.Relation.Enemy))continue;
   var pose=p.HeadPose;
   int selected=s.Bone switch {0=>0,1=>1,2=>2,3=>5,_=>-1};
   if(s.Bone==4&&p.Pose.HasHead)pose=p.Pose;
   for(int i=0;i<pose.Points.Length;i++)
   {
    if(selected>=0&&i!=selected||!pose.Valid[i])continue;
    if(!WardogReader.Project(frame.Camera,pose.Points[i],width,height,out var point)||point.X<0||point.Y<0||point.X>width||point.Y>height)continue;
    float distance=Vector2.DistanceSquared(point,center);
    if(distance>=s.Fov*s.Fov)continue;
    if(s.AimTarget==WardogAimTarget.ClosestToMe
       ? range>bestRange || (range==bestRange && distance>=best)
       : distance>=best)continue;
    bestRange=range;best=distance;result=point;player=p;worldPoint=pose.Points[i];
   }
  }
  return result;
 }
}

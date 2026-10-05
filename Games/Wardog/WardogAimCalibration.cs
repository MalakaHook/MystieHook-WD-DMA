using System.Numerics;

namespace MystieHook.Games.Wardog;

public sealed record WardogAimProfile(float DurationMs,float DeadZonePixels,float SpeedDegreesPerSecond,float TrackingErrorDegrees,float AcquisitionMs,int SuccessfulFlicks,float CameraFov,int Width,int Height)

{

 public bool LocalMouse { get; init; }
 public float ErrorPercent { get; init; }
 public WardogAimCurve Curve { get; init; }
 // One sample per aim-key press. Zero variability exactly preserves calibrated response.
 public double ResponseMultiplier(double sample) => 1 + (Math.Clamp(sample,0,1)*2-1) * ErrorPercent / 200.0;
 public double ResponseGain(double seconds,double multiplier) => 1-Math.Exp(-3*Math.Max(0,seconds)*multiplier/(DurationMs/1000.0));
 public bool Valid => Enum.IsDefined(Curve) && float.IsFinite(DurationMs)&&DurationMs is >=80 and <=5000 && float.IsFinite(ErrorPercent) && ErrorPercent is >=0 and <=100 && float.IsFinite(DeadZonePixels)&&DeadZonePixels is >=1 and <=12

 && float.IsFinite(SpeedDegreesPerSecond)&&SpeedDegreesPerSecond is >=1 and <=1500 && float.IsFinite(TrackingErrorDegrees)&&TrackingErrorDegrees>=0

 && float.IsFinite(AcquisitionMs)&&AcquisitionMs>0 && SuccessfulFlicks is >=6 and <=8 && float.IsFinite(CameraFov)&&CameraFov is >1 and <179 && Width>0&&Height>0;

}

// Measurement only; no input injection or DMA calls.

public sealed class WardogAimCalibration

{

 public enum Phase { Idle, Waiting, Countdown, Tracking, Flicks, Review, Failed }

 public Phase Stage {get;private set;}

 public bool Active => Stage is Phase.Waiting or Phase.Countdown or Phase.Tracking or Phase.Flicks;

 public bool BlocksAim => Active || Stage is Phase.Review or Phase.Failed;

 public WardogAimProfile Result {get;private set;}

 public string Message {get;private set;}="";

 public double TargetYaw {get;private set;} public double TargetPitch {get;private set;}

 public int Flick {get;private set;}

 public double Remaining {get;private set;}

 private double _time,_stageTime,_lastTime,_lastYaw,_lastPitch,_baseYaw,_basePitch,_fov,_hold,_squares,_trackingTime,_settleError;

 private int _samples,_width,_height,_successful,_settled;

 private bool _localMouse;
 private ulong _world,_controller;private long _sample;

 private readonly double[] _durations=new double[8];private readonly double[] _speeds=new double[8192];private int _speedCount;

 private static readonly Vector2[] Offsets=[new(12,4),new(-18,-5),new(8,-8),new(-10,7),new(20,0),new(-15,-4),new(6,8),new(-8,-6)];

 public void Start(bool localMouse = false){_localMouse=localMouse;Stage=Phase.Waiting;Result=null;Message=localMouse ? "Hide the menu, then follow the target with the DMA PC mouse. ESC cancels." : "Hide the menu, stay still, then follow the dot with your crosshair. ESC cancels.";_time=_stageTime=_lastTime=_hold=_squares=_trackingTime=_settleError=0;_samples=_successful=_settled=_speedCount=0;_sample=0;Flick=0;}

 public void Cancel(){Stage=Phase.Idle;Result=null;Message="Calibration cancelled.";}

 public void Accept(){Stage=Phase.Idle;Message="Profile applied. Save your configuration to keep it.";}

 private void Fail(string reason){Stage=Phase.Failed;Result=null;Message=reason;}

 public static double Angle(double a)=>((a+180)%360+360)%360-180;

 public void Sample(WardogReader.CameraFrame frame,bool menu,int width,int height,double now)

 {

  SampleCore(frame.IsFresh, frame.World, frame.Controller, frame.SampleStarted, frame.Camera.Rotation.Y,
   frame.Camera.Rotation.X, frame.Camera.Fov, menu, width, height, now);
 }
 public Vector2 PointerTarget(int width,int height) => new(
  width/2f+(float)(Math.Tan(TargetYaw*Math.PI/180)*width/2),
  height/2f-(float)(Math.Tan(TargetPitch*Math.PI/180)*width/2));
 public bool ProjectTarget(WardogReader.Camera camera,int width,int height,out Vector2 screen)
 {
  double yaw=TargetYaw*Math.PI/180,pitch=TargetPitch*Math.PI/180;
  var point=new WardogReader.Vec {X=camera.Location.X+10000*Math.Cos(pitch)*Math.Cos(yaw),
   Y=camera.Location.Y+10000*Math.Cos(pitch)*Math.Sin(yaw),Z=camera.Location.Z+10000*Math.Sin(pitch)};
  return WardogReader.Project(camera,point,width,height,out screen);
 }
 public void SamplePointer(Vector2 pointer,bool available,bool menu,int width,int height,double now)
 {
  if(!available || !float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y) || pointer.X<0 || pointer.Y<0 || pointer.X>width || pointer.Y>height)
  { if(Active)Fail("Keep the DMA PC cursor inside the overlay monitor, then retry."); return; }
  double focal=width/2.0;
  SampleCore(true,1,1,BitConverter.DoubleToInt64Bits(now),Math.Atan((pointer.X-width/2.0)/focal)*180/Math.PI,
   Math.Atan((height/2.0-pointer.Y)/focal)*180/Math.PI,90,menu,width,height,now);
 }
 private void SampleCore(bool fresh,ulong world,ulong controller,long sample,double yaw,double pitch,double fov,bool menu,int width,int height,double now)
 {
  if(!Active)return;

  if(Stage==Phase.Waiting)

  {

   if(menu)return;

   if(!fresh){Message="Waiting for fresh camera data. Open the menu to cancel.";return;}

   _world=world;_controller=controller;_baseYaw=_localMouse?0:yaw;_basePitch=_localMouse?0:pitch;

   TargetYaw=_baseYaw;TargetPitch=_basePitch;

   _fov=fov;_width=width;_height=height;_stageTime=now;Stage=Phase.Countdown;

  }

  if(menu){Fail("Calibration stopped when the menu opened. Start again when ready.");return;}

  if(!fresh||world!=_world||controller!=_controller){Fail("Camera data became stale or the game context changed. Please retry.");return;}

  if(Math.Abs(fov-_fov)>1||width!=_width||height!=_height){Fail("FOV or display size changed. Keep zoom and display settings fixed, then retry.");return;}

  if(sample==_sample)return;

  _sample=sample;

  _time=now-_stageTime;Remaining=Math.Max(0,(Stage==Phase.Countdown?3:Stage==Phase.Tracking?20:5)-_time);


  double dt=_lastTime==0?0:now-_lastTime;

  if(dt>.25){Fail("Camera sampling was interrupted. Please retry.");return;}

  if(Stage==Phase.Countdown)

  {

   Message=$"Starting in {Math.Ceiling(Remaining):0} — follow the target";

   if(_time>=3){Stage=Phase.Tracking;_stageTime=now;_baseYaw=_localMouse?0:yaw;_basePitch=_localMouse?0:pitch;}

  }

  else

  {

   if(Stage==Phase.Tracking)

   {

    TargetYaw=_baseYaw+Math.Min(12,_fov*.18)*Math.Sin(_time*1.1);

    TargetPitch=Math.Clamp(_basePitch+5*Math.Sin(_time*.73),-75,75);

   }

   double dx=Angle(TargetYaw-yaw),dy=Angle(TargetPitch-pitch),error=Math.Sqrt(dx*dx+dy*dy);

   if(dt>0)

   {

    double speed=Math.Sqrt(Math.Pow(Angle(yaw-_lastYaw),2)+Math.Pow(Angle(pitch-_lastPitch),2))/dt;

    if(_speedCount<_speeds.Length)_speeds[_speedCount++]=Math.Min(1500,speed);

    if(Stage==Phase.Tracking){_squares+=error*error*dt;_trackingTime+=dt;_samples++;}

   }

   if(Stage==Phase.Tracking)

   {

    Message=$"Tracking: {Remaining:0}s — keep following the dot";

    if(_time>=20){Stage=Phase.Flicks;NextTarget(now,yaw,pitch);}

   }

   else

   {

    Message=$"Flick {Flick+1}/8 — move onto the rectangle and hold briefly";

    double tolerance=Math.Max(.12,Math.Atan(8/(width/(2*Math.Tan(_fov*Math.PI/360))))*180/Math.PI);

    if(error<=tolerance){_hold+=dt;_settleError+=error;_settled++;}else _hold=0;

    if(_hold>=.18||_time>=5)

    {

     if(_hold>=.18)_durations[_successful++]=Math.Max(.08,_time-.18)*1000;

     Flick++;

     if(Flick==8)Finish();else NextTarget(now,yaw,pitch);

    }

   }

  }

  _lastTime=now;_lastYaw=yaw;_lastPitch=pitch;

 }

 private void NextTarget(double now,double yaw,double pitch)

 {

  _stageTime=now;_hold=0;var o=Offsets[Flick];double scale=Math.Min(1,_fov/90);

  TargetYaw=yaw+o.X*scale;TargetPitch=Math.Clamp(pitch+o.Y*scale,-75,75);
  if(_localMouse)
  {
   double horizontal=Math.Atan(Math.Max(1,_width/2-30)/(_width/2.0))*180/Math.PI;
   double vertical=Math.Atan(Math.Max(1,_height/2-30)/(_width/2.0))*180/Math.PI;
   TargetYaw=Math.Clamp(TargetYaw,-horizontal,horizontal);TargetPitch=Math.Clamp(TargetPitch,-vertical,vertical);
  }

 }

 private void Finish()

 {

  if(_successful<6||_samples<200||_trackingTime<18||_speedCount==0){Fail("Not enough usable data. Complete at least 6 of 8 flicks and the full tracking phase.");return;}

  double rms=Math.Sqrt(_squares/_trackingTime);

  if(rms>8){Fail("Tracking error was too large for a reliable profile. Retry while following the dot.");return;}

  Array.Sort(_durations,0,_successful);Array.Sort(_speeds,0,_speedCount);

  double duration=_durations[_successful/2],speed=_speeds[Math.Min(_speedCount-1,(int)(_speedCount*.9))];

  if(speed<1){Fail("Too little movement was measured. Please retry.");return;}

  double focal=_width/(2*Math.Tan(_fov*Math.PI/360));

  float dead=(float)Math.Clamp(Math.Tan((_settled>0?_settleError/_settled:.1)*Math.PI/180)*focal,2,8);

  Result=new((float)Math.Clamp(duration,80,800),dead,(float)speed,(float)rms,(float)duration,_successful,(float)_fov,_width,_height) { LocalMouse = _localMouse };

  Stage=Phase.Review;Message="Calibration complete. Open Aim to review and apply the profile.";

 }

}


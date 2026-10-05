using MystieHook.Input;
using System.Numerics;
namespace MystieHook.Games.Wardog;
// Output worker consumes completed snapshots only; never issues DMA reads.
public sealed class WardogAimController
{
 private sealed record State(WardogReader.Frame Frame,WardogAimSettings Settings,int Width,int Height,bool Menu,WardogReader.CameraFrame Camera,WardogRcsSettings Rcs);
 private State _state;
 private WardogAimTargetLock.Identity _target;
 private int _awaitRelease;
 private string _lastAttempt="No aim-key attempt recorded yet";
 public string LastAttempt=>Volatile.Read(ref _lastAttempt);
 private string _rcsAttempt="No fire-button attempt recorded. Enable a weapon profile, set a nonzero rate, hide the menu and hold fire.";
 public string RcsAttempt=>Volatile.Read(ref _rcsAttempt);
 public WardogReader.Frame LockedFrame(WardogReader.Frame frame) => Volatile.Read(ref _target)?.Filter(frame)
  ?? WardogReader.Frame.Empty("Waiting for a new aim-key press");
 private readonly object _lifecycle=new();
 private CancellationTokenSource _cancel; private Task _worker=Task.CompletedTask;
 public void Update(WardogReader.Frame frame,WardogAimSettings settings,int width,int height,bool menu,WardogReader.CameraFrame camera,WardogRcsSettings rcs=null)
 {
  rcs??=new();
  Volatile.Write(ref _state,new(frame,settings,width,height,menu,camera,rcs));
  if(!settings.Enabled){Interlocked.Exchange(ref _awaitRelease,1);Volatile.Write(ref _target,null);if(!rcs.Active)return;}
  lock(_lifecycle)if(_worker.IsCompleted){_cancel?.Dispose();_cancel=new();var token=_cancel.Token;_worker=Task.Run(()=>Run(token));}
 }
 public void Stop(){Interlocked.Exchange(ref _awaitRelease,1);Volatile.Write(ref _target,null);Volatile.Write(ref _state,null);lock(_lifecycle)_cancel?.Cancel();}
 private void Run(CancellationToken token)
 {
  var response=new WardogAimResponse();
  var targetLock=new WardogAimTargetLock();
  bool previousPrediction=false;float previousSpeed=0;
  long last=Environment.TickCount64;double remainderX=0,remainderY=0,variationSample=.5;bool wasHeld=false;
  double rcsRemainder=0;string lastWeapon="";bool outputFault=false;
  while(!token.IsCancellationRequested)
  {
   var state=Volatile.Read(ref _state);if(state==null||(!state.Settings.Enabled&&!state.Rcs.Active))return;
   long now=Environment.TickCount64;double dt=Math.Clamp((now-last)/1000.0,0,.025);last=now;
   bool held=InputManager.IsKeyDown(state.Settings.Key);
   bool firing=InputManager.IsKeyDown(1);
   bool ads=InputManager.IsKeyDown(2);
   if(!firing&&!held)outputFault=false;
   if(!held)Interlocked.Exchange(ref _awaitRelease,0);
   var settings=state.Settings;var profile=settings.UseCalibration ? settings.Calibration : null;
   bool profileMatches=settings.CalibrationReady(state.Width,state.Height,state.Frame.Camera.Fov);
   // Recheck the original independent camera age at output time, even if rendering stalls.
   bool cameraFresh=state.Camera is {IsFresh:true} && state.Camera.World==state.Frame.WorldAddress
    && state.Camera.Controller==state.Frame.LocalController;
   bool allowed=settings.Enabled&&!outputFault&&!state.Menu&&Device.connected&&profileMatches&&cameraFresh&&Volatile.Read(ref _awaitRelease)==0;
   var target=targetLock.Select(state.Frame,settings,state.Width,state.Height,held,allowed,out ulong targetAddress);
   if(held&&!state.Menu)
   {
    string reason=!Device.connected?"Blocked: Makcu disconnected"
     :!settings.UseCalibration||profile is not {Valid:true}?"Blocked: enable or apply a valid calibration"
     :profile.Width!=state.Width||profile.Height!=state.Height?$"Blocked: calibration display {profile.Width}x{profile.Height}, current {state.Width}x{state.Height}"
     :!profileMatches?"Blocked: current camera FOV is invalid for aim projection"
     :!cameraFresh?"Blocked: camera stale or context mismatch"
     :Volatile.Read(ref _awaitRelease)!=0?"Blocked: release the aim key, then press again"
     :!state.Frame.IsFresh?"Blocked: player snapshot stale"
     :targetLock.AwaitingRelease?"Blocked: locked target invalidated; release and press again"
     :settings.UsePrediction&&!state.Frame.HasPredictionSpeed?"Blocked: waiting for live bullet speed"
     :target==null?(targetLock.Target==null?"Waiting: no eligible bone inside aim FOV/range; check team and bone data":"Paused: locked target lacks an eligible bone/prediction point")
     :"Target ready; movement subject to calibration speed and dead zone";
    Volatile.Write(ref _lastAttempt,reason);
   }
   Volatile.Write(ref _target,targetLock.Target);
   if(previousPrediction!=settings.UsePrediction || (settings.UsePrediction&&previousSpeed!=state.Frame.LocalBulletSpeed))
   {response.Reset();remainderX=remainderY=0;}
   previousPrediction=settings.UsePrediction;previousSpeed=state.Frame.LocalBulletSpeed;
   if(held&&!wasHeld){variationSample=Random.Shared.NextDouble();response.Reset();}
   wasHeld=held;
   int outputX=0,outputY=0;
   if(allowed&&held&&target is Vector2 point)
   {
    double dx=point.X-state.Width/2f,dy=point.Y-state.Height/2f;
    double multiplier=profile.ResponseMultiplier(variationSample);
    double gain=response.Advance(profile,targetAddress,dt,multiplier);
    if(profileMatches && dx*dx+dy*dy<=profile.DeadZonePixels*profile.DeadZonePixels){dx=dy=0;remainderX=remainderY=0;response.Reset();}
    double x=dx*gain;
    double y=dy*gain;
    if(profileMatches)
    {
     // Camera profiles use the current zoom for the angular speed cap, including ADS.
     double focal=state.Width/(2*Math.Tan((profile.LocalMouse ? 90 : state.Frame.Camera.Fov)*Math.PI/360));
     double limit=focal*profile.SpeedDegreesPerSecond*Math.PI/180*dt*Math.Min(1,multiplier);
     double length=Math.Sqrt(x*x+y*y);if(length>limit&&length>0){x*=limit/length;y*=limit/length;}
    }
    x+=remainderX;y+=remainderY;
    int mx=(int)Math.Clamp(x,-32767,32767),my=(int)Math.Clamp(y,-32767,32767);remainderX=x-mx;remainderY=y-my;
    outputX=mx;outputY=my;
   }
   else {remainderX=remainderY=0;response.Reset();}
   string weapon=state.Frame.LocalRcsWeapon;
   if(weapon!=lastWeapon){rcsRemainder=0;lastWeapon=weapon;}
   float rate=state.Rcs.RateFor(weapon);
   if(firing&&!state.Menu)
   {
    string reason=outputFault?"Blocked: mouse output fault; release fire and aim"
     :!ads?"Blocked: hold right-click ADS together with fire"
     :!Device.connected?"Blocked: Makcu disconnected"
     :!state.Frame.IsFresh?"Blocked: player snapshot stale or unavailable"
     :!cameraFresh?"Blocked: camera stale or context mismatch"
     :string.IsNullOrEmpty(weapon)?"Blocked: held weapon could not be validated"
     :rate==0?$"Blocked: {weapon} has no enabled, nonzero profile"
     :$"Sending vertical pull for {weapon}: {rate:0} counts/s";
    Volatile.Write(ref _rcsAttempt,reason);
   }
   outputY+=WardogRcsSettings.Step(rate,!outputFault&&!state.Menu&&Device.connected&&firing&&ads&&cameraFresh&&state.Frame.IsFresh&&rate!=0,dt,ref rcsRemainder);
   try {if(!token.IsCancellationRequested&&(outputX!=0||outputY!=0))Device.move(outputX,Math.Clamp(outputY,-32767,32767));}
   catch {outputFault=true;rcsRemainder=remainderX=remainderY=0;response.Reset();targetLock.Block();Volatile.Write(ref _target,null);Volatile.Write(ref _lastAttempt,"Mouse output failed; release fire and aim, then check the device");Volatile.Write(ref _rcsAttempt,"Mouse output failed; release fire and aim, then check the device");}
   if(token.WaitHandle.WaitOne(10))return;
  }
 }
}

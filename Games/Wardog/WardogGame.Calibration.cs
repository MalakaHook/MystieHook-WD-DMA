using MystieHook.Gui;
using ImGuiNET;
using System.Numerics;
using MystieHook.Input;
using static MystieHook.OverlayWindow;
namespace MystieHook.Games.Wardog;
public sealed partial class WardogGame
{
 [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
 private WardogAimProfile _calibrationResetBaseline;
 private readonly WardogAimCalibration _calibration=new();
 private unsafe void DrawCalibrationOverlay(Vector2 size)
 {
  if(!_calibration.BlocksAim)return;
  if((GetAsyncKeyState(27) & 0x8000)!=0){_calibration.Cancel();return;}
  var camera=_monitor.Camera;
  _calibration.Sample(camera,!UiVisibility.MenusHidden,(int)size.X,(int)size.Y,ImGui.GetTime());
  if(!_calibration.BlocksAim)return;
  var vp=ImGui.GetMainViewport();var draw=ImGui.GetForegroundDrawList();var center=vp.Pos+size/2;
  if(_calibration.Stage is WardogAimCalibration.Phase.Tracking or WardogAimCalibration.Phase.Flicks)
  {
   if(camera.IsFresh && _calibration.ProjectTarget(camera.Camera,(int)size.X,(int)size.Y,out var target))
   {
    target+=vp.Pos;
    if(_calibration.Stage==WardogAimCalibration.Phase.Tracking)draw.AddCircleFilled(target,7,0xFF60E0FF);
    else draw.AddRect(target-new Vector2(9),target+new Vector2(9),0xFF60E0FF,0,ImDrawFlags.None,2);
   }
   draw.AddLine(center-new Vector2(5,0),center+new Vector2(5,0),0xFFFFFFFF);
   draw.AddLine(center-new Vector2(0,5),center+new Vector2(0,5),0xFFFFFFFF);
  }
  draw.AddText(vp.Pos+new Vector2(30,50),0xFFFFFFFF,_calibration.Message);
  draw.AddText(vp.Pos+new Vector2(30,75),0xFFFFFFFF,"Aim output paused | ESC cancels | Follow with your in-game crosshair");
 }
 private void DrawCalibrationControls()
 {
  ImGui.SeparatorText("Aim calibration");
  var s=_monitor.Aim;
  SafeText.Wrapped("Stay still in-game and follow the target with your crosshair using your gaming mouse: 20 seconds of tracking, then 8 flicks. Calibration measures game-camera response. Keep the same FOV and display size.");
  ImGui.BeginDisabled(_calibration.Active);
  if(ImGui.Button("Calibrate aim")){_aimController.Stop();_captureAimKey=false;_calibration.Start();}
  ImGui.EndDisabled();
  if(_calibration.BlocksAim){ImGui.SameLine();if(ImGui.Button("Cancel calibration"))_calibration.Cancel();}
  if(_calibration.Message.Length>0)SafeText.Wrapped(_calibration.Message);
  if(_calibration.Stage==WardogAimCalibration.Phase.Review && _calibration.Result is {} result)
  {
   if(!ReferenceEquals(_calibrationDraftSource,result))
   {
    _calibrationDraftSource=result;
    _calibrationDraft=result;
   }
   ImGui.PushID("CalibrationReview");
   _calibrationDraft=DrawCalibrationValues(_calibrationDraft);
   if(ImGui.Button("Reset to measured values"))_calibrationDraft=result;
   SafeText.Wrapped("Applying enables calibrated response. You can tune its values and save an Aim configuration below.");
   if(ImGui.Button("Apply calibration")){s=s with {Calibration=_calibrationDraft,UseCalibration=true};_calibrationResetBaseline=_calibrationDraft;_calibration.Accept();UiNotifications.Show("Calibration applied.");}
   ImGui.PopID();
  }
  if(s.Calibration is {} profile)
  {
   if(profile.LocalMouse)SafeText.Wrapped("Your saved profile uses the DMA PC mouse. Run and apply a new calibration to replace it with a game-camera profile.");
   bool use=s.UseCalibration;if(MenuTools.Checkbox("Use calibrated response",ref use))s=s with {UseCalibration=use};
   if(_calibration.Stage!=WardogAimCalibration.Phase.Review)
   {
    ImGui.PushID("SavedCalibration");
    _calibrationResetBaseline ??= profile;
    profile=DrawCalibrationValues(profile);
    if(ImGui.Button("Reset calibration edits")){profile=_calibrationResetBaseline;UiNotifications.Show("Calibration edits reset to the last applied profile.");}
    s=s with {Calibration=profile};
    ImGui.PopID();
   }
   var size=ImGui.GetMainViewport().Size;
   if(s.UseCalibration && (!profile.LocalMouse && _monitor.Camera.IsFresh && Math.Abs(_monitor.Camera.Camera.Fov-profile.CameraFov)>1 || (int)size.X!=profile.Width || (int)size.Y!=profile.Height))
    SafeText.Wrapped("Profile does not match the current FOV/display. Aim movement is paused; recalibrate or disable calibrated response.");
   if(ImGui.Button("Remove calibration"))s=s with {Calibration=null,UseCalibration=false};
  }
  SafeText.Disabled("Save below to keep these Aim settings separately. General configurations still include Aim settings too.");
  DrawAimConfigurations(ref s);
  _monitor.Aim=s;
 }
}

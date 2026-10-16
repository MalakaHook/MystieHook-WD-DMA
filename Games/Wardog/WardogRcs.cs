using ImGuiNET;
using MystieHook.Gui;
using System.Numerics;
namespace MystieHook.Games.Wardog;
public sealed record WardogRcsProfile(bool Enabled=false,float Rate=0);
public sealed record WardogRcsSettings
{
 public Dictionary<string,WardogRcsProfile> Weapons {get;init;}=new();
 public bool Active=>Weapons!=null&&Weapons.Values.Any(p=>p is {Enabled:true}&&float.IsFinite(p.Rate)&&p.Rate!=0&&Math.Abs(p.Rate)<=1000);
 public float RateFor(string weapon)=>Weapons!=null&&Weapons.TryGetValue(weapon,out var p)&&p is {Enabled:true}&&float.IsFinite(p.Rate)&&Math.Abs(p.Rate)<=1000?p.Rate:0;
 public static int Step(float rate,bool allowed,double dt,ref double remainder)
 {
  if(!allowed||!float.IsFinite(rate)||Math.Abs(rate)>1000||!double.IsFinite(dt)||dt<=0){remainder=0;return 0;}
  double pull=rate*Math.Min(dt,.025)+remainder;int step=(int)pull;remainder=pull-step;return step;
 }
}
public sealed partial class WardogGame
{
 private static readonly (string Category,string[] Weapons)[] RcsWeapons=
 [
  ("Assault",["AK74M","M4","KH2002","A91","TAR21","GALIL","BUSHMASTER M17S","TCX"]),
  ("SMG",["Vector","MP9","MP5","PP-19","PP-BIZON"]),
  ("LMG",["M249","PKM","EVO-3 ULTRA"]),
  ("Snipers",["SV98","MK22","Mosin","AMR 50","SCOUT RIFLE","RFB","SVDM","WEPN 030","M14"]),
  ("Pistols",["Glock17","Judge","M1911","G18X","DEAGLE"])
 ];
 public void DrawRcs()
 {
  SafeText.Wrapped("Hold right-click ADS and fire together for vertical movement. Negative pulls up; positive pulls down. Units are mouse counts per second. This detects held buttons, not actual shots or ammunition, and does not detect game focus.");
  var frame=_monitor.Current;
  SafeText.Wrapped($"Last RCS firing attempt: {_aimController.RcsAttempt}");
  SafeText.Disabled(MystieHook.Input.InputManager.IsKeyDown(1)?"Fire button: pressed":"Fire button: released");
  SafeText.Disabled(!_monitor.Rcs.Active?"Enable a weapon profile and set a nonzero rate to start weapon detection."
   :!frame.IsFresh?"Weapon detection: waiting for fresh player data"
   :frame.LocalRcsWeapon.Length==0?"Weapon detection: held item unresolved"
   :$"Detected weapon: {frame.LocalRcsWeapon}");
  if(!ImGui.BeginTabBar("RcsCategories"))return;
  foreach(var group in RcsWeapons)if(ImGui.BeginTabItem(group.Category))
  {
   if(ImGui.BeginTabBar("Weapons"))
   {
    foreach(var weapon in group.Weapons)if(ImGui.BeginTabItem(weapon))
    {
     ImGui.PushID(weapon);
     var settings=_monitor.Rcs;var p=settings.Weapons?.GetValueOrDefault(weapon)??new();
     bool enabled=p.Enabled;float rate=p.Rate;
     bool clicked=MenuTools.Toggle("Enabled",ref enabled);
     bool changed=ImGui.SliderFloat("Vertical pull (counts/s)",ref rate,-1000,1000,"%.0f");
     if(clicked||changed)
     {
      var copy=new Dictionary<string,WardogRcsProfile>(settings.Weapons??new());copy[weapon]=new(enabled,rate);
      _monitor.Rcs=new(){Weapons=copy};
     }
     ImGui.PopID();ImGui.EndTabItem();
    }
    ImGui.EndTabBar();
   }
   ImGui.EndTabItem();
  }
  ImGui.EndTabBar();
 }
}

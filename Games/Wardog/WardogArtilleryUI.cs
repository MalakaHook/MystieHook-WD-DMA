using MystieHook.Gui;
using MystieHook.Services;
using ImGuiNET;
using Raylib_cs;
using System.Numerics;
using static MystieHook.Games.Wardog.WardogArtilleryData;
namespace MystieHook.Games.Wardog;
public static class WardogArtilleryUI
{
 private static readonly Texture2D[] Textures=new Texture2D[3];
 private static readonly bool[] Attempted=new bool[3];
 private static MapTileCache TileCache;
 private static int TileMap=-1;
 private static string TileDirectory;
 private static readonly WardogArtilleryMarkerRefresh MarkerRefresh=new();
 public static void SuspendTiles() { TileCache?.Suspend(); }
 public static void Shutdown() { TileCache?.Dispose();TileCache=null;TileMap=-1;TileDirectory=null;for(int i=0;i<3;i++){if(Textures[i].Id!=0)Raylib.UnloadTexture(Textures[i]);Textures[i]=default;Attempted[i]=false;} }
 public static WardogArtillerySettings Draw(WardogArtillerySettings s,WardogReader.Frame f,WardogReader.VehicleFrame vf)
 {
  bool autoMap=s.AutoMap;
  MenuTools.Checkbox("Detect map automatically",ref autoMap);
  s=(s with {AutoMap=autoMap}).ApplyDetectedMap(f);
  int map=s.Map,weapon=s.Weapon,mode=s.Mode,pointMap=s.PointMap;
  bool players=s.Players,vehicles=s.Vehicles,locations=s.Locations,hasGun=s.HasGun,hasTarget=s.HasTarget;
  float zoom=s.Zoom,cx=s.CenterX,cy=s.CenterY,gx=s.GunX,gy=s.GunY,tx=s.TargetX,ty=s.TargetY;
  if(ImGui.Combo("Map",ref map,"Bakurani\0Ozeti\0Zestafona\0")){zoom=1;cx=cy=.5f;autoMap=false;}
  MarkerRefresh.Select(map,System.Diagnostics.Stopwatch.GetTimestamp());
  if(ImGui.Button("Refresh positions"))MarkerRefresh.Refresh(System.Diagnostics.Stopwatch.GetTimestamp());
  bool positionsFresh=MarkerRefresh.Accept(f);
  SafeText.Disabled(!autoMap ? "Manual map selection" : WardogArtillerySettings.HasDetectedMap(f)
      ? "Detected: "+Maps[f.DetectedMap].Name : "Map not identified yet; keeping your selection.");
  ImGui.Combo("Game artillery",ref weapon,"L81 Mortar\0SPH-2\0");
  MenuTools.Checkbox("Players",ref players);ImGui.SameLine();MenuTools.Checkbox("Vehicles",ref vehicles);ImGui.SameLine();MenuTools.Checkbox("Locations",ref locations);
  if(locations)ImGui.TextDisabled("Tower/base references from the map source; active spawns can vary by round.");
  ImGui.RadioButton("Pan",ref mode,0);ImGui.SameLine();ImGui.RadioButton("Set artillery",ref mode,1);ImGui.SameLine();ImGui.RadioButton("Set target",ref mode,2);
  var m=Maps[map];var w=Weapons[weapon];var local=World(f.LocalPosition.X,f.LocalPosition.Y,m);
  void ResetOtherMap(){if(pointMap!=map){hasGun=hasTarget=false;pointMap=map;}}
  ImGui.BeginDisabled(!positionsFresh);
  if(ImGui.Button("Use my position")){ResetOtherMap();gx=(float)local.X;gy=(float)local.Y;hasGun=true;}
  ImGui.SameLine();if(ImGui.Button("Center on me")){var uv=PointToImage(local,m);cx=(float)uv.X;cy=(float)uv.Y;}
  ImGui.EndDisabled();
  if(ImGui.Button("Fit map")){zoom=1;cx=cy=.5f;}ImGui.SameLine();if(ImGui.Button("Clear points"))hasGun=hasTarget=false;
  if(pointMap==map&&hasGun&&hasTarget)
  {
   var result=Solve(new(gx,gy),new(tx,ty),w);
   ImGui.TextUnformatted($"Distance {result.Distance:0} m | Azimuth {result.Bearing:0.0} deg");
   if(result.Low.HasValue||result.High.HasValue) ImGui.TextUnformatted($"LOW {result.Low?.ToString("0.0")??"--"} MIL | HIGH {result.High?.ToString("0.0")??"--"} MIL");
   else ImGui.TextUnformatted($"Out of supported range ({w.Min:0} - {w.Max:0} m)");
  }
  else ImGui.TextWrapped($"Place artillery and target points. Supported game range: {w.Min:0} - {w.Max:0} m.");
  if(ImGui.CollapsingHeader("Coordinates / zoom"))
  {
   ImGui.TextDisabled("1 map unit = 100 m. North = 0 degrees; east = 90 degrees.");
   bool gun=ImGui.InputFloat("Artillery X",ref gx,.1f,1,"%.2f");gun|=ImGui.InputFloat("Artillery Y",ref gy,.1f,1,"%.2f");
   bool target=ImGui.InputFloat("Target X",ref tx,.1f,1,"%.2f");target|=ImGui.InputFloat("Target Y",ref ty,.1f,1,"%.2f");
   if(gun||target){ResetOtherMap();hasGun|=gun;hasTarget|=target;}
   MenuTools.SliderFloat("Zoom",ref zoom,1,128,"%.1fx");
  }
  gx=Safe(gx);gy=Safe(gy);tx=Safe(tx);ty=Safe(ty);
  ImGui.TextDisabled("Game table interpolation; terrain height and vehicle tilt are not included.");
  if(!Attempted[map])
  {
   Attempted[map]=true;
   string path=Path.Combine(AppContext.BaseDirectory,"Assets","Maps",m.Id+".png");
   if(File.Exists(path))Textures[map]=Raylib.LoadTexture(path);
  }
  if(Textures[map].Id==0){ImGui.TextWrapped("Map image unavailable. Keep Assets/Maps beside the application.");if(ImGui.Button("Reload map images"))Shutdown();}
  var available=ImGui.GetContentRegionAvail();float size=Math.Max(120,Math.Min(available.X,Math.Max(240,available.Y-46)));
  var start=ImGui.GetCursorScreenPos();var end=start+new Vector2(size);
  ImGui.InvisibleButton("##ArtilleryMap",new(size),ImGuiButtonFlags.MouseButtonLeft|ImGuiButtonFlags.MouseButtonRight);
  bool hover=ImGui.IsItemHovered();var io=ImGui.GetIO();
  if(hover&&io.MouseWheel!=0)zoom=Math.Clamp(zoom*MathF.Pow(1.2f,io.MouseWheel),1,128);
  if(hover&&(ImGui.IsMouseDragging(ImGuiMouseButton.Right)||(mode==0&&ImGui.IsMouseDragging(ImGuiMouseButton.Left)))){cx-=io.MouseDelta.X/(size*zoom);cy-=io.MouseDelta.Y/(size*zoom);}
  float half=.5f/zoom;cx=Math.Clamp(cx,half,1-half);cy=Math.Clamp(cy,half,1-half);
  float u=cx-half,v=cy-half;
  Vector2 Screen(Point p){var uv=PointToImage(p,m);return start+new Vector2((float)uv.X-u,(float)uv.Y-v)*size*zoom;}
  Point mouse=ImageToPoint(u+(io.MousePos.X-start.X)/(size*zoom),v+(io.MousePos.Y-start.Y)/(size*zoom),m);
  if(hover&&mode!=0&&ImGui.IsMouseClicked(ImGuiMouseButton.Left))
  {ResetOtherMap();if(mode==1){gx=(float)mouse.X;gy=(float)mouse.Y;hasGun=true;}else{tx=(float)mouse.X;ty=(float)mouse.Y;hasTarget=true;}}
  var d=ImGui.GetWindowDrawList();d.PushClipRect(start,end,true);d.AddRectFilled(start,end,0xFF282319);
  if(Textures[map].Id!=0)d.AddImage((nint)Textures[map].Id,start,end,new(u,v),new(u+2*half,v+2*half));
  string directory=LocalMaps.PreparedDirectory(m.Id);
  if(TileMap!=map || TileDirectory!=directory)
  {
   TileCache?.Dispose();
   TileCache=directory==null ? null : new MapTileCache(directory);
   TileMap=map;TileDirectory=directory;
  }
  if(ImGui.IsItemVisible())TileCache?.Draw(d,start,size,zoom,u,v,MapTileLayout.MaximumLevel(m.Id));
  else TileCache?.Suspend();
  if(locations)
  {
   foreach(var location in WardogMapLocations.For(m.Id))
   {
    var p=MapTileLayout.Project(location.ImagePosition,start,size,zoom,u,v);
    d.AddCircleFilled(p,3,0xFFEBDCD2);
    if(zoom>=2)
    {
     d.AddText(p+new Vector2(7,1),0xFF000000,location.Label);
     d.AddText(p+new Vector2(6,0),0xFFF0EBE6,location.Label);
    }
   }
  }
  if(positionsFresh)
  {
   d.AddCircleFilled(Screen(local),5,0xFFFFAA50);
   if(players)foreach(var player in f.RadarPlayers)d.AddCircleFilled(Screen(World(player.Position.X,player.Position.Y,m)),3,player.TeamRelation==WardogReader.Relation.Friendly?0xFF9BDC6Eu:player.TeamRelation==WardogReader.Relation.Enemy?0xFF5F55FFu:0xFF50CDEBu);
   if(vehicles&&MarkerRefresh.Accept(vf,f.WorldAddress))foreach(var vehicle in vf.Vehicles){var p=Screen(World(vehicle.Position.X,vehicle.Position.Y,m));d.AddRect(p-new Vector2(4),p+new Vector2(4),0xFF3C91FF,0,ImDrawFlags.None,2);}
  }
  if(pointMap==map)
  {
   var gun=Screen(new(gx,gy));var target=Screen(new(tx,ty));
   if(hasGun){d.AddCircleFilled(gun,5,0xFF9BE655);d.AddText(gun,0xFFFFFFFF,"Artillery");d.AddCircle(gun,(float)(w.Max*100/ImageBounds(m).Size)*size*zoom,0xA09BE655,128);}
   if(hasTarget){d.AddCircle(target,6,0xFF50DCFF,16,2);d.AddText(target,0xFFFFFFFF,"Target");}
   if(hasGun&&hasTarget)d.AddLine(gun,target,0xFF64EBFF,2);
  }
  d.PopClipRect();d.AddRect(start,end,0xFF917D64);
  if(!positionsFresh)SafeText.Disabled("Waiting for fresh map positions...");
  if(hover)ImGui.TextUnformatted($"X {mouse.X:0.00} Y {mouse.Y:0.00} | Wheel: zoom, right drag: pan");else ImGui.TextDisabled("Select the current map. Wheel: zoom. Right drag: pan.");
  if(TileCache!=null)ImGui.TextUnformatted($"Map detail: level {TileCache.RequestedLevel} | {TileCache.ResidentCount} cached tiles | {TileCache.MissingCount} awaiting detail");
  return s with {AutoMap=autoMap,Map=map,Weapon=weapon,Mode=mode,Players=players,Vehicles=vehicles,Locations=locations,Zoom=zoom,CenterX=cx,CenterY=cy,PointMap=pointMap,HasGun=hasGun,HasTarget=hasTarget,GunX=gx,GunY=gy,TargetX=tx,TargetY=ty};
 }
 private static float Safe(float value)=>float.IsFinite(value)?Math.Clamp(value,-50000,50000):0;
}

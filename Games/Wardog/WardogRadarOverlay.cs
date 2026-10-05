using ImGuiNET;
using System.Numerics;
using MystieHook.Gui;
using MystieHook.Services;
namespace MystieHook.Games.Wardog;

// Graphics-thread owner. Tile decoding stays in the bounded background cache.
public static class WardogRadarOverlay
{
    private static Vector2 _lastPosition = new(float.NaN);
    private static MapTileCache _tiles;
    private static string _directory;
    private static int _map=-1;
    private static bool _positionLoaded;
    private static readonly WardogArtilleryMarkerRefresh Refresh=new();
    public static void Shutdown(){_tiles?.Dispose();_tiles=null;_directory=null;_map=-1;}
    public static Vector2 Project(WardogReader.Vec world,WardogReader.Vec local,int map,double yaw,float pixels,float zoom)
    {
        var m=WardogArtilleryData.Maps[map];
        var a=WardogArtilleryData.WorldToImage(world.X,world.Y,m);
        var b=WardogArtilleryData.WorldToImage(local.X,local.Y,m);
        return MapTileLayout.Rotate(new((float)(a.X-b.X)*pixels*zoom,(float)(a.Y-b.Y)*pixels*zoom),Vector2.Zero,Angle(yaw));
    }
    public static float Angle(double yaw)=>(float)(-Math.PI/2-yaw*Math.PI/180);
    public static uint FactionColor(string faction)=>faction?.ToLowerInvariant() switch
    {"manticore"=>0xFF50C850u,"valkyra"=>0xFF5050EFu,"lonestar"=>0xFFFF9650u,_=>0xFFAAAAAAu};
    public static Vector2 FacingDirection(double facing,double cameraYaw)
    {
        double angle=(facing-cameraYaw)*Math.PI/180-Math.PI/2;
        return new((float)Math.Cos(angle),(float)Math.Sin(angle));
    }
    public static (Vector2 Tip,Vector2 Left,Vector2 Right) Triangle(Vector2 center,Vector2 direction)
    {
        var side=new Vector2(-direction.Y,direction.X);
        return (center+direction*8,center-direction*5+side*5,center-direction*5-side*5);
    }
    private static void DrawTriangle(ImDrawListPtr draw,Vector2 center,Vector2 direction,uint color)
    {
        var t=Triangle(center,direction);
        draw.AddTriangleFilled(t.Tip,t.Left,t.Right,color);
        draw.AddTriangle(t.Tip,t.Left,t.Right,0xFF202020,1);
    }
    public static WardogRadarSettings Draw(WardogReader.Frame frame,WardogReader.VehicleFrame vehicles,
        WardogRadarSettings r,bool menuOpen)
    {
        if(!r.Enabled){_tiles?.Suspend();return r;}
        if(!_positionLoaded && WardogRadarPosition.Loaded.IsCompletedSuccessfully)
        {
            _positionLoaded=true;
            if(WardogRadarPosition.Loaded.Result is { } saved)r=r with {X=saved.X,Y=saved.Y};
        }
        r=r.ApplyDetectedMap(frame);
        Refresh.Select(r.Map,System.Diagnostics.Stopwatch.GetTimestamp());
        var vp=ImGui.GetMainViewport();
        float size=Math.Max(1,Math.Min(r.Size,Math.Min(vp.Size.X,vp.Size.Y)));
        var pos=new Vector2(Math.Clamp(r.X,0,Math.Max(0,vp.Size.X-size)),Math.Clamp(r.Y,0,Math.Max(0,vp.Size.Y-size)));
        if(pos!=_lastPosition)ImGui.SetNextWindowPos(vp.Pos+pos,ImGuiCond.Always);
        ImGui.SetNextWindowSize(new(size,size),ImGuiCond.Always);
        var flags=ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoBackground;
        if(!menuOpen)flags|=ImGuiWindowFlags.NoInputs;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize,0);
        if(ImGui.Begin("###WardogMinimap",flags))
        {
            pos=ImGui.GetWindowPos()-vp.Pos;
            var start=ImGui.GetCursorScreenPos();var end=start+new Vector2(size);var center=(start+end)/2;
            var draw=ImGui.GetWindowDrawList();draw.AddRectFilled(start,end,0xFF181810);draw.PushClipRect(start,end,true);
            var map=WardogArtilleryData.Maps[r.Map];
            string directory=LocalMaps.PreparedDirectory(map.Id);
            if(_map!=r.Map || _directory!=directory){Shutdown();_map=r.Map;_directory=directory;_tiles=new MapTileCache(directory,Path.Combine(AppContext.BaseDirectory,"Assets","Maps",map.Id+".png"));}
            bool fresh=Refresh.Accept(frame) && frame.LocalPosition.Valid() && double.IsFinite(frame.Camera.Rotation.Y);
            if(fresh)
            {
                var local=WardogArtilleryData.WorldToImage(frame.LocalPosition.X,frame.LocalPosition.Y,map);
                _tiles?.Draw(draw,start,size,r.Zoom,(float)local.X-.5f/r.Zoom,(float)local.Y-.5f/r.Zoom,MapTileLayout.MaximumLevel(map.Id),Angle(frame.Camera.Rotation.Y));
                bool Point(WardogReader.Vec world,out Vector2 point)
                {
                    point=center+Project(world,frame.LocalPosition,r.Map,frame.Camera.Rotation.Y,size,r.Zoom);
                    return float.IsFinite(point.X)&&float.IsFinite(point.Y)&&point.X>=start.X&&point.X<=end.X&&point.Y>=start.Y&&point.Y<=end.Y;
                }
                if(r.Players)foreach(var player in frame.RadarPlayers)
                    if(!player.Dead && (r.Teammates || player.TeamRelation!=WardogReader.Relation.Friendly) && Point(player.Position,out var point))
                    {
                        uint color=FactionColor(player.Faction);
                        if(player.FacingYaw is double facing&&double.IsFinite(facing))
                            DrawTriangle(draw,point,FacingDirection(facing,frame.Camera.Rotation.Y),color);
                        else draw.AddCircleFilled(point,4,color);
                        if(r.Names)draw.AddText(point+new Vector2(6,0),color,player.Name??"");
                    }
                if(r.Vehicles && Refresh.Accept(vehicles,frame.WorldAddress))
                    foreach(var vehicle in vehicles.Vehicles)
                        if((r.Teammates || vehicle.TeamRelation!=WardogReader.Relation.Friendly)&&Point(vehicle.Position,out var point))
                            draw.AddRectFilled(point-new Vector2(4),point+new Vector2(4),0xFF4691FF);
                DrawTriangle(draw,center,new(0,-1),FactionColor(frame.LocalFaction));
            }
            else {_tiles?.Suspend();draw.AddText(start+new Vector2(8),0xFFFFFFFF,"Waiting for fresh camera / position");}
            draw.PopClipRect();
            ImGui.InvisibleButton("Drag minimap",new(size,size));
            if(menuOpen && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                pos+=ImGui.GetIO().MouseDelta;
                pos=Vector2.Clamp(pos,Vector2.Zero,Vector2.Max(Vector2.Zero,vp.Size-new Vector2(size)));
                ImGui.SetWindowPos(vp.Pos+pos);
            }
            if(ImGui.IsItemDeactivated())WardogRadarPosition.Save(pos);
        }
        else _tiles?.Suspend();
        ImGui.End();ImGui.PopStyleVar(2);_lastPosition=pos;
        return r with {X=Math.Clamp(pos.X,0,16000),Y=Math.Clamp(pos.Y,0,16000)};
    }
}

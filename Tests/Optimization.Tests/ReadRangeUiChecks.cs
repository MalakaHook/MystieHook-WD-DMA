using System.Reflection;
using MystieHook.Games.Wardog;
internal static class ReadRangeUiChecks
{
    public static void Run(Action<bool,string> check)
    {
        var finish=typeof(WardogMonitor).GetMethod("FinalizeRadar",BindingFlags.Static|BindingFlags.NonPublic)!;
        var friend=new WardogReader.Player(1,"friend",default,100){TeamRelation=WardogReader.Relation.Friendly};
        var enemy=new WardogReader.Player(2,"enemy",default,100){TeamRelation=WardogReader.Relation.Enemy};
        var source=new WardogReader.Frame("ready",true,default,default,[]){RadarPlayers=[friend,enemy],WebRadarPlayers=[friend,enemy]};
        WardogReader.Frame Finalize(WardogReader.Frame f,bool aim,bool localMap)=>(WardogReader.Frame)finish.Invoke(null,
            [f,new WardogVisualSettings(ShowTeammates:false),aim,new WardogRadarSettings(Enabled:true){LocalMap=localMap}])!;
        check(Finalize(source,true,true).RadarPlayers.SequenceEqual(source.RadarPlayers),
            "aim/prediction post-processing preserves independently requested local radar teammates");
        check(Finalize(source,false,true).RadarPlayers.SequenceEqual(source.RadarPlayers),
            "local radar teammate positions remain independent of hidden ESP teammates without aim");
        check(Finalize(source,true,false).RadarPlayers.SequenceEqual(new[]{enemy})
            &&Finalize(source,true,false).WebRadarPlayers.SequenceEqual(source.WebRadarPlayers),
            "legacy visual filtering and independent web-radar output are preserved");
        check(Finalize(source with{RadarPlayers=[enemy]},true,true).RadarPlayers.SequenceEqual(new[]{enemy}),
            "post-processing does not add teammates excluded by the radar's own toggle");
        check(Enumerable.Range(0,3).All(map=>new WardogRadarSettings{Map=map,Zoom=1}.CaptureRange<=1000),
            "zooming the minimap out cannot expand the game data range beyond 1000m");
        var monitor=new WardogMonitor();monitor.Radar=new(Enabled:true,Names:true){Zoom=1};monitor.VehicleSettings=new(true,true,30000);
        monitor.SetWebRadarDemand(true);monitor.SetMapDemand(true,true);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var radar=(WardogRadarSettings)typeof(WardogMonitor).GetProperty("AcquisitionRadar",flags)!.GetValue(monitor)!;
        var vehicleMonitor=typeof(WardogMonitor).GetField("_vehicles",flags)!.GetValue(monitor)!;
        var vehicles=(WardogVehicleSettings)vehicleMonitor.GetType().GetProperty("Settings")!.GetValue(vehicleMonitor)!;
        check(radar.Range==1000&&radar.WebSharing&&radar.Names&&vehicles.MaximumDistance==1000,
            "web/map sharing preserves demand and authorization flow while capping player and vehicle requests at 1000m");
        monitor.SetWebRadarDemand(false);monitor.SetMapDemand(false,false);
        vehicles=(WardogVehicleSettings)vehicleMonitor.GetType().GetProperty("Settings")!.GetValue(vehicleMonitor)!;
        check(vehicles.MaximumDistance==1000&&!monitor.IsRunning,"ordinary expanded vehicle settings are capped without starting a connection");
    }
}

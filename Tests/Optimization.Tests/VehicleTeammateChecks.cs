using System.Diagnostics;
using MystieHook.Games.Wardog;
using O=MystieHook.Games.Wardog.WardogOffsets;
internal static class VehicleTeammateChecks
{
 public static void Run(Action<bool,string> check)
 {
  const ulong vehicle=0xB00000,type=0xB10000,root=0xB20000,level=0xB30000,array=0xB40000,
   seat=0xB50000,seatType=0xB60000,op=0xB70000,opType=0xB80000;
  var f=new Fixture();f.Teams();f.Now=1000;
  f.Put(Fixture.RemoteFaction+O.FactionComponentTag,111UL);
  f.Put(Fixture.Chunk+O.VehicleBaseClassIndex*O.ObjectItemSize+O.ObjectItemObject,type);
  f.Array(Fixture.World+O.WorldLevels,0,0);f.Put(Fixture.World+O.WorldPersistentLevel,level);
  f.Array(level+0xA0,array,1);f.Put(array,vehicle);f.Put(vehicle+O.UObjectClass,type);f.Put(vehicle+O.ActorRootComponent,root);
  f.Name("WDStationaryVehicle",10000);f.Put(type+O.ClassNameId,10000u);f.Put(root+O.SceneRelativeLocation,new WardogReader.Vec{X=1000});
  f.Name("BHVehicleSeatComponent",10100);f.Put(seatType+O.ClassNameId,10100u);f.Put(seat+O.UObjectClass,seatType);
  f.Name("BHVehicleOperatorComponent",10200);f.Put(opType+O.ClassNameId,10200u);f.Put(op+O.UObjectClass,opType);
  // Literal current offsets with old-field decoys independently catch drift.
  f.Put(vehicle+0x758,seat);f.Put(vehicle+0x748,0xBA0000UL);
  f.Put(Fixture.Actor+0x768,op);f.Put(Fixture.Actor+0x758,0xBB0000UL);f.Put(op+0x198,seat);
  var reader=f.Reader();var settings=new WardogVehicleSettings(true,false,1000);
  WardogReader.Frame Context()=>new("fixture",true,new(default,default,90),default,[])
   {WorldAddress=Fixture.World,LocalPawn=Fixture.Pawn,LocalController=Fixture.Controller,SampleStarted=Stopwatch.GetTimestamp()};
  check(O.VehicleSeatComponent==0x758&&O.VehicleOperatorComponent==0x768,"GitHub vehicle fields match the working main layout");
  check(reader.CaptureVehicles(settings,Context()).Vehicles.Length==0,"teammate in current operator/seat layout hides vehicle with no visual teammate demand");
  check(reader.CaptureVehicles(settings with {ShowTeammateVehicles=true},Context()).Vehicles.Length==1,"show teammate vehicles explicitly restores occupied vehicle");
  f.Put(Fixture.RemoteFaction+O.FactionComponentTag,222UL);f.Now+=600;
  check(reader.CaptureVehicles(settings,Context()).Vehicles.Length==1,"enemy occupant remains visible with teammate vehicles disabled");
  f.Put(Fixture.RemoteFaction+O.FactionComponentTag,111UL);f.Now+=600;
  check(reader.CaptureVehicles(settings,Context()).Vehicles.Length==0,"fresh teammate relation hides vehicle again");
  f.Put(Fixture.Actor+0x768,0UL);f.Now+=600;
  check(reader.CaptureVehicles(settings,Context()).Vehicles.Length==1,"exiting clears friendly occupancy instead of hiding the empty vehicle");
  f.Put(Fixture.State+O.PlayerStatePawn,vehicle);f.Now+=600;
  check(reader.CaptureVehicles(settings,Context()).Vehicles.Length==0,"teammate possessing the vehicle uses its current seat field");
  var manual=typeof(MystieHook.Input.Device).GetMethod("MakcuConnect")!.GetParameters();
  check(manual.Length==2&&manual[1].HasDefaultValue&&Equals(manual[1].DefaultValue,false),"manual custom firmware mode defaults off");
  check(typeof(MystieHook.Input.Device).GetMethod("AutoConnectMakcu")!.GetParameters().Length==0,"automatic MAKCU discovery has no handshake bypass option");
 }
}

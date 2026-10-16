using System.Diagnostics;
using MystieHook.Games.Wardog;
using O=MystieHook.Games.Wardog.WardogOffsets;
internal static class ReadRangeChecks
{
    static WardogReader.Frame Context()=>new("fixture",true,new(default,default,90),default,[])
        {WorldAddress=Fixture.World,LocalController=Fixture.Controller,LocalPawn=Fixture.Pawn,SampleStarted=Stopwatch.GetTimestamp()};
    public static void Run(Action<bool,string> check)
    {
        check(WardogReadRange.Contains(new(){X=100000},default)&&!WardogReadRange.Contains(new(){X=100000.01},default)
            &&!WardogReadRange.Contains(new(){X=double.NaN},default)&&!WardogReadRange.Contains(new(){X=80000,Y=80000},default),
            "shared 1000m cutoff is inclusive, finite and uses actual 3D distance");
        foreach(bool optimized in new[]{false,true})
        {
            var f=new Fixture();f.Bones();f.Teams();
            foreach(ulong page in f.Memory.Keys.Select(a=>a&~4095UL).Distinct().ToArray())
                for(ulong n=0;n<4096;n++)f.Memory.TryAdd(page+n,0);
            var pages=new HashSet<ulong>();
            bool Direct(ulong address,Span<byte> bytes){pages.Add(address&~4095UL);return f.Read(address,bytes);}
            var memory=new WardogFrameMemory(Direct,requests=>{foreach(var r in requests){pages.Add(r.Address);r.Success=f.Read(r.Address,r.Bytes);}},optimized);
            var reader=new WardogReader(Fixture.Base,memory.Read,Direct,()=>f.Now,memory.ScheduleNext,memory.Prefetch,optimizedReads:optimized);
            WardogReader.Frame Capture(double x)
            {
                f.Now+=100;f.Put(Fixture.ActorRoot+O.SceneRelativeLocation,new WardogReader.Vec{X=x});f.Transform(Fixture.Mesh+0x1D0,x,0,0);
                pages.Clear();memory.BeginFrame();try{return reader.Capture(new(ShowBones:true,MaximumDistance:5000),
                    radar:new(Enabled:true,Names:true,Range:30000){WebSharing=true,LocalMap=true});}finally{memory.EndFrame();}
            }
            var far=Capture(100001);
            bool NoDetail()=>!new[]{Fixture.Mesh,Fixture.Poses,Fixture.Text,Fixture.HealthVitality,Fixture.HealthAttribute,Fixture.RemoteFaction}.Any(a=>pages.Contains(a&~4095UL));
            check(far.Players.Length==0&&far.RadarPlayers.Length==0&&far.WebRadarPlayers.Length==0&&NoDetail(),
                "outside 1000m neither ESP nor expanded radar demand fetches player detail pages: "+optimized);
            check(Capture(100000).Players.Single().Pose.HasHead&&pages.Contains(Fixture.Text),
                "player exactly at 1000m can acquire fresh name and bones: "+optimized);
            check(Capture(100001).Players.Length==0&&NoDetail(),"moving out of range stops detail page transfers on the next player scan: "+optimized);
            f.Rename("Reentered");
            check(Capture(99900).Players.Single().Name=="Reentered", "returning player reacquires a fresh name instead of reviving removed far metadata: "+optimized);
        }
        const ulong actor=0xB00000,type=0xB10000,root=0xB20000,level=0xB30000,array=0xB40000;
        var vehicle=new Fixture();vehicle.Teams();vehicle.Put(Fixture.Chunk+O.VehicleBaseClassIndex*O.ObjectItemSize+O.ObjectItemObject,type);
        vehicle.Array(Fixture.World+O.WorldLevels,0,0);vehicle.Put(Fixture.World+O.WorldPersistentLevel,level);
        vehicle.Array(level+0xA0,array,1);vehicle.Put(array,actor);vehicle.Put(actor+O.UObjectClass,type);vehicle.Put(actor+O.ActorRootComponent,root);
        vehicle.Name("WDStationaryVehicle",10000);vehicle.Put(type+O.ClassNameId,10000u);
        vehicle.Put(root+O.SceneRelativeLocation,new WardogReader.Vec{X=100001});
        vehicle.Put(Fixture.ActorRoot+O.SceneRelativeLocation,new WardogReader.Vec{X=100001});
        var vr=vehicle.Reader();var vs=new WardogVehicleSettings(true,false,30000){CaptureTeams=true};
        check(vr.CaptureVehicles(vs,Context()).Vehicles.Length==0&&!vehicle.Reads.ContainsKey(actor+O.VehicleMesh)
            &&!vehicle.Reads.ContainsKey(actor+0x748)&&!vehicle.Reads.ContainsKey(Fixture.Actor+0x758)
            &&!vehicle.Reads.ContainsKey(Fixture.RemoteFaction+O.FactionComponentTag),
            "expanded vehicle requests cannot fetch far model, seat or remote occupant team detail");
        vehicle.Put(root+O.SceneRelativeLocation,new WardogReader.Vec{X=100000});vehicle.Now+=600;
        check(vr.CaptureVehicles(vs,Context()).Vehicles.Length==1,"vehicle exactly at 1000m remains eligible after returning");
        ContactRetirement(check);
    }
    static void ContactRetirement(Action<bool,string> check)
    {
        var waves=new List<ulong[]>();
        var batch=new WardogReadBatch((ulong _,Span<byte> bytes)=>{bytes.Fill(7);return true;},requests=>
            {waves.Add(requests.Select(r=>r.Address).ToArray());foreach(var r in requests){r.Bytes.AsSpan().Fill(7);r.Success=true;}})
            {ScheduledRangeRetentionMilliseconds=250};
        void Read(ulong address)=>batch.Read(address,new byte[8]);
        batch.BeginFrame(0);batch.ScheduleNext(200);batch.SetContact(1);Read(0x10000);Read(0x30000);
        batch.SetContact(2);Read(0x20000);Read(0x30000);batch.SetContact(0);batch.ScheduleNext(0);Read(0x40000);batch.EndFrame();
        batch.BeginFrame(50);batch.ForgetContact(1);batch.EndFrame();waves.Clear();
        batch.BeginFrame(200);
        var fetched=waves.SelectMany(p=>p).ToArray();
        check(!fetched.Contains(0x10000UL)&&fetched.Contains(0x20000UL)&&fetched.Contains(0x30000UL),
            "forgetting a distant contact retires its future detail prefetch while preserving another unvisited contact and shared ranges");
        batch.SetContact(2);Read(0x20000);Read(0x30000);batch.SetContact(0);batch.ForgetContact(2);
        check(batch.VerifyUsed(),"retired contact ranges still participate in the independent proof before removal");
        batch.EndFrame();waves.Clear();batch.BeginFrame(250);
        check(!waves.SelectMany(p=>p).Any(a=>a is 0x10000 or 0x20000 or 0x30000),"last contact owner removal stops later shared detail prefetch");batch.EndFrame();
        batch.BeginFrame(300);batch.SetContact(1);check(batch.Read(0x10000,new byte[8]),"returning contact can relearn a retired range freshly");batch.EndFrame();
    }
}

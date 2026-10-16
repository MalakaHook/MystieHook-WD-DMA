using System.Diagnostics;
using MystieHook.Games.Wardog;
using F = MystieHook.Games.Wardog.WardogReader.Frame;
using V = MystieHook.Games.Wardog.WardogReader.Vec;
using P = MystieHook.Games.Wardog.WardogReader.Player;

internal static class PipelineChecks
{
    public static void Run(Action<bool,string> check)
    {
        int transfers=0,directs=0; byte fill=7; bool partial=false;
        bool Direct(ulong address,Span<byte> bytes) {directs++;bytes.Fill(99);return !partial;}
        void Scatter(IReadOnlyCollection<WardogReadBatch.Request> requests)
        {transfers++;foreach(var r in requests){r.Bytes.AsSpan().Fill(fill);r.Success=!partial;}}
        var memory=new WardogFrameMemory(Direct,Scatter);
        byte[] buffer=new byte[8];
        memory.BeginFrame();memory.Prefetch([new(0x10100,16),new(0x10200,20)]);
        check(memory.BatchedReads==1&&transfers==1,"reference pages deduplicate overlapping fields");
        check(memory.Read(0x10100,buffer)&&buffer.All(x=>x==7)&&directs==0&&transfers==1,"cached page serves fields without extra transfer");
        fill=8;memory.BeginFrame();memory.Read(0x10100,buffer);
        check(buffer.All(x=>x==8)&&transfers==2,"page contents are discarded on every scan");
        memory.BeginFrame();memory.Read(0x10FFC,buffer);
        check(memory.BatchedReads==2&&buffer.All(x=>x==8),"cross-page reads copy both pages completely");
        partial=true;memory.BeginFrame();buffer.AsSpan().Fill(10);
        check(!memory.Read(0x10100,buffer)&&buffer.All(x=>x==0)&&directs==1,"failed page and direct read cannot publish old bytes");
        int count=transfers;memory.Read(0x10100,buffer);
        check(transfers==count,"failed page is not redundantly prefetched within a scan");
        check(!memory.Read(ulong.MaxValue-3,buffer),"overflowing range rejected before hardware access");
        var cancelled=new WardogFrameMemory(Direct,_=>throw new OperationCanceledException());
        bool threw=false;try{cancelled.Read(0x10100,buffer);}catch(OperationCanceledException){threw=true;}
        check(threw,"page-cache cancellation propagates without publication");

        long now=Stopwatch.GetTimestamp();long Ms(int value)=>(long)(Stopwatch.Frequency*value/1000.0);
        F Frame(int ago,double x)=>new("ok",true,new(new V{X=x},default,90),default,
            [new P(0x10000,"synthetic",new V{X=x},100){SourceState=1,SourceMesh=2,SourceAsset=3}])
            {WorldAddress=1,LocalController=2,LocalPawn=3,SampleStarted=now-Ms(ago),CompletedAt=now-Ms(ago)};
        var history=new WardogVisualMotion();var a=Frame(40,0);var b=Frame(20,100);
        history.Push(a);history.Push(b);
        var sample=history.Sample(now);
        check(Math.Abs(sample.Players[0].Position.X-75)<.01&&sample.Camera==b.Camera,"only world-space actors interpolate; scan camera is never delayed");
        check(b.Players[0].Position.X==100&&sample.SampleStarted==b.SampleStarted,"interpolation does not mutate raw data or renew sample time");
        check(!history.Sample(now+Ms(140)).Valid,"smooth display expires 150 ms after publication");
        check(history.Sample(now+Ms(200),false).Valid&&!history.Sample(now+Ms(500),false).Valid,"raw display holds until 500 ms publication expiry only");
        var stale=Frame(0,0) with {SampleStarted=now-Ms(400)};
        history.Clear();history.Push(stale);var held=history.Sample(now,false);
        check(held.Valid&&!held.IsFresh&&held.IsDisplayFresh,"held display remains distinguishable from fresh acquisition");
        check(!WardogReader.WithCamera(held,new(1,2,held.Camera,held.SampleStarted)).Valid,"held display cannot pass the raw input freshness gate");
        history.Clear();history.Push(a);history.Push(b with {Players=[]});
        check(history.Sample(now).Players.Length==0,"interpolation never resurrects a missing actor");
        history.Clear();history.Push(a);history.Push(b with {Players=[b.Players[0] with {SourceMesh=99}]});
        check(history.Sample(now).Players[0].Position.X==100,"mesh identity change blocks pose and position interpolation");
        history.Clear();history.Push(a);history.Push(b with {WorldAddress=99});
        check(history.Sample(now).Camera.Location.X==100,"world change clears prior interpolation history");
        history.Push(F.Empty("lost"));check(!history.Sample(now).Valid,"invalid context clears held display immediately");
        history.Clear();history.Push(a);history.Push(b with {Camera=new(new V{X=5000},default,90)});
        check(history.Sample(now).Camera.Location.X==5000,"camera teleport snaps instead of interpolating");
        history.Clear();history.Push(a);history.Push(b);
        var liveCamera=new WardogReader.CameraFrame(1,2,new(new V{X=130},new V{Y=35},100),Stopwatch.GetTimestamp());
        var drawing=history.SampleForCamera(now,liveCamera);
        check(drawing.Camera==liveCamera.Camera && Math.Abs(drawing.Players[0].Position.X-75)<.01,
            "render composition uses latest camera translation rotation and FOV with smoothed actors");
        check(drawing.SampleStarted==b.SampleStarted && drawing.CompletedAt==b.CompletedAt,
            "fresh camera never renews either player acquisition or completion timestamp");
        check(history.SampleForCamera(now,liveCamera,false).Camera==liveCamera.Camera,
            "camera is equally current when player smoothing is disabled");
        check(!history.SampleForCamera(now,liveCamera with{World=99}).Valid &&
            !history.SampleForCamera(now,liveCamera with{Controller=99}).Valid,"render rejects world and controller mismatch");
        check(!history.SampleForCamera(now,liveCamera with{SampleStarted=now-Ms(110)}).Valid,
            "render rejects camera older than 100 ms instead of falling back to delayed scan camera");
        history.Clear();history.Push(stale);
        check(!history.SampleForCamera(now,liveCamera,false).Valid,"fresh camera cannot make a held stale player frame drawable");
        history.Clear();history.Push(b with{Players=[b.Players[0] with{Position=new V{X=1000}}]});
        var firstView=history.SampleForCamera(now,liveCamera with{Camera=new(default,default,90)});
        var turnedView=history.SampleForCamera(now,liveCamera with{Camera=new(default,new V{Y=20},90)});
        WardogReader.Project(firstView.Camera,firstView.Players[0].Position,1920,1080,out var firstPixel);
        WardogReader.Project(turnedView.Camera,turnedView.Players[0].Position,1920,1080,out var turnedPixel);
        WardogReader.Project(new(default,new V{Y=20},90),turnedView.Players[0].Position,1920,1080,out var expectedPixel);
        check(firstPixel!=turnedPixel && turnedPixel==expectedPixel && firstView.SampleStarted==turnedView.SampleStarted,
            "stationary player's screen position responds immediately to a new camera without waiting for another player scan");
        var options=new Dictionary<VmmSharpEx.Options.VmmOption,ulong?> {
            [VmmSharpEx.Options.VmmOption.CONFIG_IS_REFRESH_ENABLED]=1,
            [VmmSharpEx.Options.VmmOption.CONFIG_TICK_PERIOD]=100,
            [VmmSharpEx.Options.VmmOption.CONFIG_READCACHE_TICKS]=3,
            [VmmSharpEx.Options.VmmOption.CONFIG_TLBCACHE_TICKS]=20,
            [VmmSharpEx.Options.VmmOption.CONFIG_PROCCACHE_TICKS_PARTIAL]=50,
            [VmmSharpEx.Options.VmmOption.CONFIG_PROCCACHE_TICKS_TOTAL]=150};
        int writes=0;
        MystieHook.Services.VmmRefreshExperiment.Initialize(key=>options[key],(key,value)=>{writes++;options[key]=value;return true;});
        var refresh=MystieHook.Services.VmmRefreshExperiment.Current;
        check(writes==2&&refresh.After.PartialProcessTicks==1000000&&refresh.After.TotalProcessTicks==1000000,
            "production startup applies reference process cadence");
        check(refresh.After==refresh.Before with {PartialProcessTicks=1000000,TotalProcessTicks=1000000},
            "reference initialization leaves TLB, memory, tick and refresh enabled state unchanged");
    }
}


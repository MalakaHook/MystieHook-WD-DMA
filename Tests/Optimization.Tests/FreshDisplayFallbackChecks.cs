using System.Diagnostics;
using MystieHook.Games.Wardog;
using F=MystieHook.Games.Wardog.WardogReader.Frame;
using V=MystieHook.Games.Wardog.WardogReader.Vec;
using P=MystieHook.Games.Wardog.WardogReader.Player;
using Issue=MystieHook.Games.Wardog.WardogVisualMotion.DisplayIssue;

internal static class FreshDisplayFallbackChecks
{
    public static void Run(Action<bool,string> check)
    {
        long now=Stopwatch.GetTimestamp();long Ms(int n)=>(long)(n*Stopwatch.Frequency/1000d);
        var player=new P(123,"fixture",new V{X=1000},100);
        var frame=new F("ready",true,new(default,default,90),default,[player])
            {SampleStarted=now-Ms(190),CompletedAt=now-Ms(180),WorldAddress=1,LocalController=2,LocalPawn=3};
        var camera=new WardogReader.CameraFrame(1,2,new(default,new V{Y=20},90),now);
        var history=new WardogVisualMotion();history.Push(frame);
        check(!history.Sample(now).Valid,"fallback does not extend the existing 150ms history retention");
        check(!history.SampleForCamera(now,camera,out var disabled,freshFallback:false).Valid
            &&disabled.Issues==Issue.DisplayExpired,"disabled fallback preserves previous display-expiry behavior");
        check(history.SampleForCamera(now,camera).Valid==WardogReadOptimization.Enabled,
            "raw display fallback defaults on only in the optimization build");
        var drawn=history.SampleForCamera(now,camera,out var probe,freshFallback:true);
        check(drawn.Valid&&drawn.IsFresh&&drawn.IsDisplayFresh&&probe.Drawable&&probe.UsedRawFallback&&probe.Issues==Issue.None,
            "fresh raw snapshot bridges display-only expiry after 150ms");
        check(drawn.SampleStarted==frame.SampleStarted&&drawn.CompletedAt==frame.CompletedAt&&drawn.DisplayUntil==0,
            "fallback preserves original acquisition/completion stamps and creates no extended display lifetime");
        check(ReferenceEquals(drawn.Players,frame.Players)&&drawn.Players.Single()==player&&drawn.Camera==camera.Camera,
            "fallback snaps to original raw player data with current camera, without extrapolation");
        foreach(int age in new[]{250,251,500})
        {
            history.Clear();history.Push(frame with{SampleStarted=now-Ms(age),CompletedAt=now-Ms(180)});
            check(!history.SampleForCamera(now,camera,out probe,freshFallback:true).Valid&&!probe.UsedRawFallback,
                "fallback rejects raw player acquisition age at/over 250ms: "+age);
        }
        history.Clear();history.Push(frame);
        foreach(var badCamera in new[]{camera with{SampleStarted=now-Ms(100)},camera with{SampleStarted=now-Ms(110)},
            camera with{World=9},camera with{Controller=9},camera with{SampleStarted=now+Ms(10)},WardogReader.CameraFrame.Empty})
            check(!history.SampleForCamera(now,badCamera,out probe,freshFallback:true).Valid&&!probe.UsedRawFallback,
                "fallback rejects stale/missing/future/mismatched camera");
        history.Push(F.Empty("changed context"));
        check(!history.SampleForCamera(now,camera,out probe,freshFallback:true).Valid&&!probe.UsedRawFallback
            &&probe.Issues.HasFlag(Issue.NoPlayerFrame),"cleared/invalid history cannot resurrect an older raw snapshot");
        history.Push(frame with{CompletedAt=now+Ms(10)});
        check(!history.SampleForCamera(now,camera,out probe,freshFallback:true).Valid&&!probe.UsedRawFallback,
            "future completion cannot enter the display-expiry fallback");
        history.Clear();history.Push(frame with{SampleStarted=now+Ms(10)});
        check(!history.SampleForCamera(now,camera,out probe,freshFallback:true).Valid&&!probe.UsedRawFallback,
            "future acquisition cannot enter the display-expiry fallback");
        history.Clear();history.Push(frame with{SampleStarted=now-Ms(20),CompletedAt=now-Ms(10)});
        var normal=history.SampleForCamera(now,camera,out probe,freshFallback:true);
        check(normal.Valid&&!probe.UsedRawFallback&&normal.DisplayUntil!=0,"normal fresh history retains its existing interpolation path");
        history.Push(frame with{WorldAddress=9,SampleStarted=now-Ms(10),CompletedAt=now});
        check(!history.SampleForCamera(now,camera,out probe,freshFallback:true).Valid,
            "world changes invalidate old-context fallback eligibility");
    }
}


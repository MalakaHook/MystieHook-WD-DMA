using System.Diagnostics;
using MystieHook.Games.Wardog;
using MystieHook.Services;
using F=MystieHook.Games.Wardog.WardogReader.Frame;
using Issue=MystieHook.Games.Wardog.WardogVisualMotion.DisplayIssue;

internal static class RenderGapChecks
{
    public static void Run(Action<bool,string> check)
    {
        long now=Stopwatch.GetTimestamp();long Ms(int n)=>(long)(n*Stopwatch.Frequency/1000d);
        var frame=new F("ready",true,default,default,[]){SampleStarted=now-Ms(190),CompletedAt=now-Ms(180),WorldAddress=1,LocalController=2};
        var camera=new WardogReader.CameraFrame(1,2,default,now);
        var history=new WardogVisualMotion();history.Push(frame);
        var result=history.SampleForCamera(now,camera,out var probe,freshFallback:false);
        check(!result.Valid&&probe.Issues==Issue.DisplayExpired
            &&SessionDiagnostics.Classify(frame,camera,true,now)==DataIssue.None,
            "display history expiry is distinguished from matching fresh raw snapshots");
        check(Math.Abs(probe.CompletionAgeMs.Value-180)<.01&&Math.Abs(probe.PlayerAgeMs.Value-190)<.01,
            "display diagnostics preserve acquisition and completion ages separately");
        frame=frame with {SampleStarted=now-Ms(20),CompletedAt=now-Ms(10)};history.Clear();history.Push(frame);
        check(history.SampleForCamera(now,camera,out probe).Valid&&probe.Drawable&&probe.Issues==Issue.None,
            "fresh display instrumentation preserves drawable frames");
        history.SampleForCamera(now,camera with{SampleStarted=now-Ms(110)},out probe);
        check(probe.Issues.HasFlag(Issue.CameraStale)&&!probe.Drawable,"display probe attributes stale camera separately");
        history.SampleForCamera(now,WardogReader.CameraFrame.Empty,out probe);
        check(probe.Issues.HasFlag(Issue.CameraUnavailable)&&probe.CameraAgeMs is null,"missing camera remains unknown rather than zero age");
        history.SampleForCamera(now,camera with{World=3},out probe);
        check(probe.Issues.HasFlag(Issue.ContextMismatch),"display probe attributes mismatched context without logging identities");
        history.Push(F.Empty("lost"));history.SampleForCamera(now,camera,out probe);
        check(probe.Issues.HasFlag(Issue.NoPlayerFrame)&&probe.PlayerAgeMs is null,"cleared display history is distinguished from raw player expiry");
        history.Push(frame with{CompletedAt=now+Ms(1000)});history.SampleForCamera(now,camera,out probe);
        check(probe.Issues.HasFlag(Issue.FutureCompletion),"future completion is explicitly classified without renewing timestamps");
        var gaps=new WardogRenderGapDiagnostics();
        var failed=new WardogVisualMotion.DisplayProbe(Issue.DisplayExpired,false,190,180,1);
        gaps.Observe(now,failed,DataIssue.None);
        gaps.Observe(now+Ms(10),failed with{Issues=Issue.CameraStale,CameraAgeMs=110},DataIssue.CameraStale);
        gaps.Observe(now+Ms(20),failed with{Drawable=true,Issues=Issue.None},DataIssue.None);
        var entries=gaps.Drain();var gap=entries.Single();
        check(gap.Frames==2&&gap.Recovered&&gap.Issues==(Issue.DisplayExpired|Issue.CameraStale)
            &&gap.RawIssues==DataIssue.CameraStale&&gap.CameraAgeMs==110,
            "render queue retains brief gaps, recovery and combined observed causes");
        check(gaps.Drain().Length==0,"render-gap drain does not repeat already reported episodes");
        for(int i=0;i<40;i++){gaps.Observe(now+Ms(i*2),failed,DataIssue.None);gaps.Observe(now+Ms(i*2+1),failed with{Drawable=true},DataIssue.None);}
        check(gaps.Drain().Length==32,"render-gap episode history remains bounded under repeated flicker");
        check(WardogRenderGapDiagnostics.Describe(gap,now+Ms(30)).Contains("rawIssues=CameraStale"),
            "render log reports display and raw snapshot classifications separately");
    }
}


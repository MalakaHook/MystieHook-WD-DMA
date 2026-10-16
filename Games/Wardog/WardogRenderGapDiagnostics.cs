using System.Diagnostics;
using MystieHook.Services;

namespace MystieHook.Games.Wardog;

// Actual UI display failures only. The UI queues numeric/enum data, never files.
public sealed class WardogRenderGapDiagnostics
{
    public sealed record Gap(long Started,long Last,int Frames,WardogVisualMotion.DisplayIssue Issues,
        DataIssue RawIssues,double? PlayerAgeMs,double? CompletionAgeMs,double? CameraAgeMs,bool Recovered);
    private readonly object _sync=new();
    private readonly List<Gap> _gaps=new();
    private bool _failed;
    private static double? Peak(double? a,double? b)=>a is null?b:b is null?a:Math.Max(a.Value,b.Value);
    public void Observe(long now,WardogVisualMotion.DisplayProbe probe,DataIssue rawIssues)
    {
        lock(_sync)
        {
            if(probe.Drawable)
            {
                if(_failed&&_gaps.Count>0)_gaps[^1]=_gaps[^1] with {Last=now,Recovered=true};
                _failed=false;return;
            }
            if(_failed&&_gaps.Count>0)
            {
                var old=_gaps[^1];
                _gaps[^1]=old with {Last=now,Frames=old.Frames+1,Issues=old.Issues|probe.Issues,
                    RawIssues=old.RawIssues|rawIssues,PlayerAgeMs=Peak(old.PlayerAgeMs,probe.PlayerAgeMs),
                    CompletionAgeMs=Peak(old.CompletionAgeMs,probe.CompletionAgeMs),CameraAgeMs=Peak(old.CameraAgeMs,probe.CameraAgeMs)};
            }
            else
            {
                if(_gaps.Count==32)_gaps.RemoveAt(0);
                _gaps.Add(new(now,now,1,probe.Issues,rawIssues,probe.PlayerAgeMs,probe.CompletionAgeMs,probe.CameraAgeMs,false));
            }
            _failed=true;
        }
    }
    public Gap[] Drain() {lock(_sync){var result=_gaps.ToArray();_gaps.Clear();_failed=false;return result;}}
    private static string Age(double? value)=>value.HasValue?value.Value.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"ms":"unknown";
    public static string Describe(Gap gap,long now)=>FormattableString.Invariant(
        $"Render gap: issues={gap.Issues}; rawIssues={gap.RawIssues}; frames={gap.Frames}; recovered={gap.Recovered}; startedAgo={(now-gap.Started)*1000d/Stopwatch.Frequency:0.0}ms; observedSpan={(gap.Last-gap.Started)*1000d/Stopwatch.Frequency:0.0}ms; playerAge={Age(gap.PlayerAgeMs)}; completedAge={Age(gap.CompletionAgeMs)}; cameraAge={Age(gap.CameraAgeMs)}");
}

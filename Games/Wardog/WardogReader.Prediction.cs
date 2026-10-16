using System.Diagnostics;
namespace MystieHook.Games.Wardog;

public sealed partial class WardogReader
{
    // Consume a completed background sample. Discovery and numerical sampling
    // never run on the camera/render/player scan callbacks.
    private (float Speed,long Stamp,string Status,bool Cached) ReadPredictionSpeed(ulong world,ulong controller,ulong pawn,ulong pawnOffset,bool enabled,long started)
    {
        (float,long,string,bool) Reject(string reason)=>(0,0,reason,false);
        if(!enabled)return Reject("Bullet-speed sampling disabled");
        var sample=predictionSpeed?.Invoke();
        if(sample==null)return Reject("Waiting for live bullet speed");
        if(sample.World!=world||sample.Controller!=controller||sample.Pawn!=pawn)
            return Reject(sample.World==0?sample.Status:"Bullet-speed sample belongs to another player context");
        if(!sample.HasUsableSpeed)return Reject(sample.FreshnessFailure);
        if(sample.Guards.Length==0)return Reject("Bullet-speed weapon identity unavailable");
        var requests=sample.Guards.Select(p=>new WardogReadBatch.Request(p.Address,p.Bytes.Length)).ToArray();
        if(requests.Length>256||requests.Sum(r=>r.Bytes.Length)>65536||requests.Any(r=>r.Bytes.Length>4096))
            return Reject("Bullet-speed identity verification exceeds read bounds");
        MarkReadOperation();
        if(verifyScatter!=null)verifyScatter(requests);
        else foreach(var request in requests)request.Success=(readFresh??read)(request.Address,request.Bytes);
        for(int index=0;index<requests.Length;index++)
            if(!requests[index].Success||!requests[index].Bytes.AsSpan().SequenceEqual(sample.Guards[index].Bytes))
                return Reject("Held weapon changed or its identity could not be rechecked");
        if(!sample.HasUsableSpeed)return Reject(sample.FreshnessFailure+" during weapon verification");
        if(Stopwatch.GetElapsedTime(started).TotalMilliseconds>200)
            return Reject("Player scan exceeded 200 ms during bullet-speed verification");
        return (sample.Speed,sample.Stamp,sample.Status,sample.CachedForWeapon);
    }
}

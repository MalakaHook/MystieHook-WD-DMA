namespace MystieHook.Services;
public sealed record FeatureColors(uint? Box,uint? Bones,uint? Head,uint AimLine,uint Prediction)
{
    public static FeatureColors Capture(UserConfiguration c){var w=c.Wardog;return new(w.Visuals.BoxColor,w.Visuals.BoneColor,w.Visuals.HeadDotColor,w.Aim.LineColor,w.Aim.HeadPredictionColor);}
    public void Apply(UserConfiguration c){var w=c.Wardog;w.Visuals=w.Visuals with {BoxColor=Box,BoneColor=Bones,HeadDotColor=Head};w.Aim=w.Aim with {LineColor=AimLine,HeadPredictionColor=Prediction};}
}
// Owned by the UI thread. One background writer; immutable color snapshots only.
// Flush before manual saves/profile transitions/exit to preserve write ordering.
public sealed class FeatureColorAutosave(ConfigurationStore store)
{
    public sealed record Result(string Profile,FeatureColors Colors,string Error);
    private sealed record Pending(string Profile,FeatureColors Colors,double Due);
    private Pending _pending;
    private Task<Result> _work;
    public void Request(string profile,FeatureColors colors,double now)
        =>_pending=new(profile,colors,now+.4);
    private Result Write(Pending item)
    {
        try {var saved=store.Load(item.Profile);item.Colors.Apply(saved);store.Save(item.Profile,saved);return new(item.Profile,item.Colors,null);}
        catch(Exception ex){return new(item.Profile,item.Colors,ex.Message);}
    }
    public Result Tick(double now,bool editing)
    {
        Result result=null;
        if(_work is {IsCompleted:true}){result=_work.GetAwaiter().GetResult();_work=null;}
        if(_work==null&&_pending!=null&&!editing&&now>=_pending.Due)
        {var item=_pending;_pending=null;_work=Task.Run(()=>Write(item));}
        return result;
    }
    public Result[] Flush()
    {
        var results=new List<Result>();
        if(_work!=null){results.Add(_work.GetAwaiter().GetResult());_work=null;}
        if(_pending!=null){var item=_pending;_pending=null;results.Add(Write(item));}
        return results.ToArray();
    }
}


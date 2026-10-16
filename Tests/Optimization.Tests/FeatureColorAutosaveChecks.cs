using System.Text.Json;
using MystieHook.Services;

internal static class FeatureColorAutosaveChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var store=new ConfigurationStore(Path.Combine(root,"color-autosave"));
        var original=new UserConfiguration();original.Wardog.Visuals=original.Wardog.Visuals with{MaximumDistance=400};
        store.Save("One",original);store.Save("Two",new());
        var current=store.Load("One");current.Wardog.Visuals=current.Wardog.Visuals with{MaximumDistance=800,BoxColor=123,BoneColor=456,HeadDotColor=789};
        current.Wardog.Aim=current.Wardog.Aim with{LineColor=111,HeadPredictionColor=222};
        var colors=FeatureColors.Capture(current);var autosave=new FeatureColorAutosave(store);
        autosave.Request("One",colors,0);autosave.Tick(.3,false);
        check(store.Load("One").Wardog.Visuals.BoxColor==original.Wardog.Visuals.BoxColor,"color save waits for the debounce interval");
        autosave.Tick(1,true);
        check(store.Load("One").Wardog.Visuals.BoxColor==original.Wardog.Visuals.BoxColor,"color drag does not write while still editing");
        autosave.Tick(1,false);var done=autosave.Flush().Single();
        var saved=store.Load("One");
        check(done.Error==null&&FeatureColors.Capture(saved)==colors,"all five feature colors automatically round-trip");
        check(saved.Wardog.Visuals.MaximumDistance==400,"color autosave preserves unrelated unsaved settings");
        check(FeatureColors.Capture(store.Load("Two"))!=colors,"color autosave does not touch another profile");
        var first=colors with{Box=12};var last=colors with{Box=null,Bones=100};
        autosave.Request("One",first,2);autosave.Request("One",last,2.1);autosave.Flush();
        check(FeatureColors.Capture(store.Load("One"))==last,"rapid edits coalesce to the final color including nullable team-color defaults");
        autosave.Request("One",first,3);autosave.Flush();autosave.Request("Two",last,3.1);autosave.Flush();
        check(FeatureColors.Capture(store.Load("One"))==first&&FeatureColors.Capture(store.Load("Two"))==last,
            "flushing before profile changes keeps each profile's own colors");
        autosave.Request("One",colors,4);autosave.Tick(5,false);autosave.Request("One",last,5.1);
        var flushed=autosave.Flush();
        check(flushed.Length==2&&FeatureColors.Capture(store.Load("One"))==last,"exit/manual-save flush waits for in-flight work then the latest pending edit");
        autosave.Request("Missing",colors,6);var failed=autosave.Flush().Single();
        check(failed.Error!=null&&!store.List().Contains("Missing"),"write failures surface without creating the wrong profile");
        var snapshot=JsonSerializer.Deserialize<UserConfiguration>(JsonSerializer.Serialize(original))!;colors.Apply(snapshot);
        check(FeatureColors.Capture(snapshot)==colors&&snapshot.Wardog.Visuals.MaximumDistance==400,
            "saved/Revert baseline can update colors without marking unrelated edits saved");
    }
}


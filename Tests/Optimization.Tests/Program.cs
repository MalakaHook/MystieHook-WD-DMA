using System.Reflection;
using System.Text.Json;
using ImGuiNET;
using MystieHook.Games.Wardog;
using MystieHook.Services;

int passed=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
PlayerNameChecks.Run(Check);
FreshDisplayFallbackChecks.Run(Check);
RenderGapChecks.Run(Check);
PredictionWeaponChecks.Run(Check);
FeatureColorAutosaveChecks.Run(Check,Path.Combine(AppContext.BaseDirectory,"followup-colors"));
ReadOptimizationChecks.Run(Check);
HiddenTeammateChecks.Run(Check);
ReadRangeChecks.Run(Check);
VehicleTeammateChecks.Run(Check);
ReadRangeUiChecks.Run(Check);
PipelineChecks.Run(Check);

var policy=typeof(WardogMonitor).GetMethod("AcquisitionVisuals",BindingFlags.Static|BindingFlags.NonPublic)!;
var visual=new WardogVisualSettings(ShowTeammates:false);
WardogVisualSettings Settings(bool teamCheck)=>(WardogVisualSettings)policy.Invoke(null,[visual,new WardogAimSettings{Enabled=true,TeamCheck=teamCheck}])!;
Check(!Settings(true).ShowTeammates&&Settings(false).ShowTeammates,"aim preserves explicit teammate demand without forcing hidden teammate reads when Team check is on");

var metrics=new WardogReadStatistics();metrics.Record(WardogReadStream.Players,2,8192,2,1,2);
var directory=Path.Combine(Environment.GetEnvironmentVariable("WARDOG_TEST_TEMP_ROOT")??AppContext.BaseDirectory,"export-"+Guid.NewGuid().ToString("N"));
var path=await DiagnosticsExport.WriteAsync(DiagnosticsExport.CaptureSettings(new()),new(),directory,metrics.Snapshot());
using(var doc=JsonDocument.Parse(await File.ReadAllTextAsync(path)))
{
    bool enabled=doc.RootElement.TryGetProperty("ReadOptimization",out var telemetry);
    Check(enabled==WardogReadOptimization.Enabled,"export includes optimization metrics only when that build mode is enabled");
    if(enabled)Check(telemetry.GetProperty("Statistics")[0].GetProperty("RequestedBytes").GetInt64()==8192,"export carries bounded requested-byte metrics");
    Check(!doc.RootElement.ToString().Contains("0x10000"),"performance export contains no fixture memory address");
}
var catalog=((string Category,string[] Weapons)[])typeof(WardogGame).GetField("RcsWeapons",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
foreach(var item in new[]{("037","TCX","Assault"),("031","PP-BIZON","SMG"),("036","EVO-3 ULTRA","LMG"),("034","M14","Snipers")})
    Check(WardogWeaponTable.HeldItemLabel("Id.Item.WEPN_"+item.Item1)==item.Item2&&catalog.Single(g=>g.Category==item.Item3).Weapons.Contains(item.Item2),"held identity and RCS category agree for "+item.Item2);
Check(!WardogReadOptimization.LiteBatching,"rejected Lite batching stays disabled");
NativeSettings(Check);
Console.WriteLine($"All {passed} optimization port checks passed. No game or DMA connection was opened.");

static unsafe void NativeSettings(Action<bool,string> check)
{
    var previous=ImGui.GetCurrentContext();var context=ImGui.CreateContext();
    try
    {
        var io=ImGui.GetIO();io.NativePtr->IniFilename=null;io.DisplaySize=new(1200,900);io.DeltaTime=1f/60;
        io.Fonts.AddFontDefault();io.Fonts.GetTexDataAsRGBA32(out IntPtr _,out int _,out int _,out int _);
        var game=new WardogGame{AutoConnect=false};
        var monitor=typeof(WardogGame).GetField("_monitor",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(game)!;
        var statistics=new WardogReadStatistics();statistics.Record(WardogReadStream.Players,2,8192,2,1,2);
        typeof(WardogMonitor).GetField("_readStatistics",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(monitor,statistics);
        ImGui.NewFrame();ImGui.SetNextWindowSize(io.DisplaySize);ImGui.Begin("Optimization settings fixture");game.DrawSettings();ImGui.End();ImGui.Render();
        check(ImGui.GetDrawData().TotalVtxCount>0&&!game.IsAttached&&game.ReadStatistics.Length==1,
            "actual Settings panel renders seeded optimization metrics without hardware initialization");
    }
    finally{ImGui.DestroyContext(context);ImGui.SetCurrentContext(previous);}
}



using System.Diagnostics;
using System.Runtime.InteropServices;
using O = MystieHook.Games.Wardog.WardogOffsets;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogReader
{
    private long _nextMapRead, _mapSampleStarted;
    private int _detectedMap = -1;
    public sealed record MapIdentityDebug(long SampleStarted,string World,string Package,string Level,string LevelOuter,string LevelPackage,string Experience,string Status)
    {
        private static string Label(string value)=>string.IsNullOrEmpty(value)?"(unresolved)":value;
        public string Report=>$"World: {Label(World)}\nWorld package: {Label(Package)}\nPersistent level: {Label(Level)}\nLevel outer: {Label(LevelOuter)}\nLevel package: {Label(LevelPackage)}\nExperience: {Label(Experience)}\nDetection: {Status}";
    }
    private MapIdentityDebug _mapDebug;

    private bool ReadMapFresh<T>(ulong address, out T value) where T : unmanaged
    {
        value = default;
        if (!Pointer(address)) return false;
        Span<T> result = stackalloc T[1];
        MarkReadOperation();
        if (!(readFresh ?? read)(address, MemoryMarshal.AsBytes(result))) return false;
        value = result[0];
        return true;
    }

    public static int MapFromIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 512) return -1;
        int result = -1;
        // Exact live experience aliases confirmed by the user's per-battlefield reports.
        // Broad world names (Europe/NorthAmerica) do not identify a battlefield.
        foreach(string part in identifier.Split(['/', '\\'],StringSplitOptions.RemoveEmptyEntries))
        {
            int alias=part.ToLowerInvariant() switch {"madrid_koth_01"=>1,"detroit_koth_01"=>2,_=>-1};
            if(alias<0)continue;
            if(result>=0 && result!=alias)return -1;
            result=alias;
        }
        foreach (string token in identifier.Split(['/', '\\', '.', '_', '-', ':', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if(token.Equals("Frontend",StringComparison.OrdinalIgnoreCase))return -1;
            int candidate = token.ToLowerInvariant() switch { "bakurani" => 0, "ozeti" => 1, "zestafona" => 2, _ => -1 };
            if (candidate < 0) continue;
            if (result >= 0 && result != candidate) return -1;
            result = candidate;
        }
        return result;
    }

    private void UpdateMapIdentity(ulong world, ulong gameState, bool enabled)
    {
        if (!enabled) { _mapDebug=null; _detectedMap = -1; _mapSampleStarted = _nextMapRead = 0; return; }
        if (_now < _nextMapRead) return;
        _nextMapRead = _now + 1000;
        _detectedMap = -1;
        _mapSampleStarted = 0;
        long started = Stopwatch.GetTimestamp();
        scheduleRead?.Invoke(0); // Mutable world/experience fields are never scheduled as immutable.
        var names=new List<(ulong Address,uint Id)>();
        string Name(ulong address)
        {
            if(!Pointer(address)||!Read(address+O.UObjectNameId,out uint id))return "";
            names.Add((address+O.UObjectNameId,id));return DisplayObjectName(id);
        }
        string worldName = Name(world);
        // Frontend may have playable experiences/assets loaded; it is not a match.
        if (worldName.Equals("Frontend", StringComparison.OrdinalIgnoreCase))
        {_mapDebug=new(started,worldName,"","","","","","Frontend; no battlefield");return;}
        ulong package = Ptr(world + 0x20); // UObject::Outer, supplied dumper.
        string packageName = Name(package);
        // Only the current persistent level and two outers; never scan loaded levels/assets.
        ulong level=Ptr(world+O.WorldPersistentLevel),outer=level!=0?Ptr(level+0x20):0;
        ulong levelPackage=outer!=0 && outer!=world?Ptr(outer+0x20):0;
        string levelName=Name(level),outerName=Name(outer),levelPackageName=Name(levelPackage);
        // AWDGameState::ExperienceManagerComponent +328, current definition +E0.
        ulong manager = Ptr(gameState + 0x328);
        string experience = "";
        string experienceStatus="No initialized experience";
        bool stable=true;
        if (manager != 0 && Read(manager + 0x170, out byte initialized) && initialized == 1
            && Read(manager + 0xE0, out uint experienceId))
        {
            experience = DisplayObjectName(experienceId);
            experienceStatus="Initialized experience";
            if (!ReadMapFresh(manager + 0xE0, out uint checkId) || checkId != experienceId
                || !ReadMapFresh(manager + 0x170, out byte checkInitialized) || checkInitialized != 1
                || !ReadMapFresh(gameState + 0x328, out ulong checkManager) || checkManager != manager) stable=false;
        }
        foreach(var name in names)if(!ReadMapFresh(name.Address,out uint checkName)||checkName!=name.Id)stable=false;
        if(package!=0 && (!ReadMapFresh(world+0x20,out ulong p)||p!=package))stable=false;
        if(level!=0 && (!ReadMapFresh(world+O.WorldPersistentLevel,out ulong l)||l!=level))stable=false;
        if(outer!=0 && (!ReadMapFresh(level+0x20,out ulong o)||o!=outer))stable=false;
        if(levelPackage!=0 && (!ReadMapFresh(outer+0x20,out ulong p2)||p2!=levelPackage))stable=false;
        // Combining active identifiers rejects conflicts without scanning loaded assets.
        _detectedMap = stable?MapFromIdentifier(worldName + "/" + packageName + "/" + levelName + "/" + outerName + "/" + levelPackageName + "/" + experience):-1;
        if (_detectedMap >= 0) _mapSampleStarted = started;
        _mapDebug=new(started,worldName,packageName,levelName,outerName,levelPackageName,experience,
            !stable?"Names changed during sample; rejected":_detectedMap<0?"Unknown or conflicting names; "+experienceStatus:new[]{"Bakurani","Ozeti","Zestafona"}[_detectedMap]);
    }
}

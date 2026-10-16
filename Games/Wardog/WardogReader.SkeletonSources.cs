using System.Runtime.InteropServices;
using O = MystieHook.Games.Wardog.WardogOffsets;
namespace MystieHook.Games.Wardog;

public sealed partial class WardogReader
{
    private readonly record struct PoseDiagnostic(ulong Asset,ulong Offset,int Count,int Valid,long Seen);
    private readonly Dictionary<ulong,PoseDiagnostic> _poseDiagnostics=new();
    private void RecordPoseSource(ulong mesh,ulong asset,ulong offset,int count,BoneMap map,int valid)
    {
        if(_poseDiagnostics.Count>=128&&!_poseDiagnostics.ContainsKey(mesh))_poseDiagnostics.Clear();
        _poseDiagnostics[mesh]=new(asset,offset,count,valid,_now);
    }
    public string DescribeSkeletons()
    {
        var result=new System.Text.StringBuilder();
        result.Append($"namePoolLayout={_nameLayout}; cachedNames={_nameCache.Count}; assetMaps={_maps.Count}");
        int shown=0;
        foreach(var entry in _poseDiagnostics)
        {
            var p=entry.Value;if(_now-p.Seen>2000)continue;
            if(shown++>=8)break;
            _maps.TryGetValue(p.Asset,out var map);
            result.Append($" | mesh=0x{entry.Key:X} asset=0x{p.Asset:X} pose=0x{p.Offset:X} count={p.Count} valid={p.Valid}");
            if(map==null)result.Append(" mapping=unavailable/fixed-fallback");
            else
            {
                result.Append($" ref=0x{map.Header:X} resolved={map.ResolvedCount}/{map.Infos.Length} usable={map.Usable} head={map.Selected[0]} neck={map.Selected[1]} chest={map.Selected[2]} pelvis={map.Selected[5]}");
                for(int n=0;n<Math.Min(4,map.Infos.Length);n++)
                { uint id=map.Infos[n].Name; result.Append($" bone{n}=0x{id:X}:{_nameCache.GetValueOrDefault(id,"<unresolved>")}"); }
            }
        }
        return result.ToString();
    }
    private static bool IsHelperBone(string name) => new[] { "ik_", "twist", "cloth", "correct", "weapon", "gear", "attach", "hair", "backpack", "armor", "strap", "helper" }.Any(name.Contains);
    // Candidate locations from the supplied reader. Discover one per cycle,
    // validate its hierarchy, then cache the exact header per asset.
    private readonly Dictionary<ulong, int> _boneDiscovery = new();
    private static readonly ulong[] ReferenceOffsets = [0x2E0,0x2F8,0x300,0x2D8,0x2E8,0x308,0x2C8];
    private bool DiscoverBoneInfo(ulong asset, out ulong header, out Arr array, out BoneInfo[] infos)
    {
        int candidate = _boneDiscovery.GetValueOrDefault(asset);
        if (_boneDiscovery.Count >= 256) _boneDiscovery.Clear();
        _boneDiscovery[asset] = (candidate+1)%11;
        header = candidate < ReferenceOffsets.Length ? asset+ReferenceOffsets[candidate] : 0;
        if (candidate >= ReferenceOffsets.Length)
        {
            ulong skeleton = Ptr(asset+0x118);
            if (skeleton != 0) header = skeleton + new ulong[] {0x48,0x20,0x28,0x38}[candidate-7];
        }
        array = default; infos = [];
        if (header == 0 || !Read(header, out array) || !Pointer(array.Data)
            || array.Count < 16 || array.Count > 1024 || array.Capacity < array.Count || array.Capacity > 4096) return false;
        infos = new BoneInfo[array.Count];
        MarkReadOperation();
        if (!read(array.Data, MemoryMarshal.AsBytes(infos.AsSpan()))) return false;
        // Parent-before-child excludes cycles and unrelated arrays.
        return infos.Select((v,i) => v.Parent >= -1 && v.Parent < i).All(v => v);
    }
    private static bool ValidPoseArray(Arr array) => Pointer(array.Data) && array.Count >= 10
        && array.Count <= 1024 && array.Capacity >= array.Count && array.Capacity <= 4096;
    private IEnumerable<ulong> PoseOffsets(ulong mesh)
    {
        // 0x630 is the editable-buffer selector, never another TArray.
        if (Read(mesh+0x630, out int editing) && editing is 0 or 1)
            yield return 0x610UL + (ulong)(1-editing)*0x10;
        else
        {
            yield return 0x610;
            yield return 0x620;
        }
        yield return 0xA18; // Cached component-space pose.
        yield return 0xA08; // Local-space pose requires the reference parents.
    }
    private sealed record BodyMesh(ulong Mesh, ulong Asset, long Expires);
    private readonly Dictionary<ulong, BodyMesh> _bodyMeshes = new();
    private int _bodyBudget;
    private int PoseCount(ulong mesh)
    {
        if (mesh == 0) return 0;
        int count = 0;
        foreach (ulong offset in PoseOffsets(mesh))
            if (Read(mesh+offset,out Arr array) && ValidPoseArray(array)) count = Math.Max(count,array.Count);
        return count;
    }
    private ulong MeshAsset(ulong mesh)
    {
        ulong asset = Ptr(mesh+O.SkinnedAsset);
        return asset != 0 ? asset : Ptr(mesh+O.SkinnedAssetSecondary);
    }
    private ulong LeaderMesh(ulong mesh)
    {
        // The current +10 object pointer overlaps the legacy +10 weak serial.
        // Do not interpret pointer bytes as a serial until the new serial layout is verified.
        ulong objectPointerOffset = O.ObjectItemObject;
        if (objectPointerOffset == 0x10) return 0;
        if (!Read(mesh+0x5B8,out int index) || index < 0
            || !Read(mesh+0x5BC,out int serial) || serial <= 0
            || !Read(moduleBase+O.NumElements,out int count) || index >= count) return 0;
        ulong chunks = Ptr(moduleBase+O.GObjects);
        ulong chunk = chunks == 0 ? 0 : Ptr(chunks+(ulong)(index>>16)*8);
        if (chunk == 0) return 0;
        ulong item = chunk+(ulong)(index&0xFFFF)*O.ObjectItemSize;
        // Legacy +8 object layout only. Require matching weak serial.
        if (!Read(item+0x10,out int actual) || actual != serial) return 0;
        return Ptr(item+O.ObjectItemObject);
    }
    private ulong SelectBodyMesh(ulong source)
    {
        if (source == 0) return 0;
        if (_bodyMeshes.TryGetValue(source,out var cached) && _now < cached.Expires
            && MeshAsset(cached.Mesh) == cached.Asset) return cached.Mesh;
        if (_bodyBudget <= 0) return source;
        _bodyBudget--;
        ulong chosen = source;
        int best = PoseCount(source);
        if (best < 148)
        {
            var candidates = new HashSet<ulong> { Ptr(source+0xF0), LeaderMesh(source) };
            if (Read(source+0x108,out Arr children) && Pointer(children.Data)
                && children.Count > 0 && children.Count <= 64 && children.Capacity >= children.Count)
                for (int i=0;i<Math.Min(children.Count,8);i++) candidates.Add(Ptr(children.Data+(ulong)i*8));
            foreach (ulong candidate in candidates)
            {
                if (candidate == 0 || candidate == source || MeshAsset(candidate) == 0) continue;
                int count = PoseCount(candidate);
                if (count > best) { best = count; chosen = candidate; }
            }
        }
        if (_bodyMeshes.Count >= 256) _bodyMeshes.Clear();
        _bodyMeshes[source] = new(chosen,MeshAsset(chosen),_now+4000);
        return chosen;
    }
}

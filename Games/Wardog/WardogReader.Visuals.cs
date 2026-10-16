using System.Runtime.InteropServices;
using System.Text;
using O = MystieHook.Games.Wardog.WardogOffsets;

namespace MystieHook.Games.Wardog;

// Team and animated-pose decoding adapted from legacy Reader.h; see ThirdParty notice.
public sealed partial class WardogReader
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct Squad(uint A, uint B, uint C, uint D);
    public readonly record struct TeamIdentity(ulong FactionTag, Squad Squad)
    {
        public string FactionNameTag { get; init; }
        public string DisplayName => FactionNameTag switch
        {
            "Meta.Alignment.Faction.Alpha" => "Lonestar",
            "Meta.Alignment.Faction.Bravo" => "Valkyra",
            "Meta.Alignment.Faction.Charlie" => "Manticore",
            _ => "Unknown"
        };
        public bool Known => FactionTag != 0 || Squad != default;
    }
    public static Relation CompareTeams(TeamIdentity a, TeamIdentity b)
    {
        // Either complete, nonzero identity can establish friendship.
        if (a.Squad != default && a.Squad == b.Squad) return Relation.Friendly;
        if (a.FactionTag != 0 && b.FactionTag != 0)
            return a.FactionTag == b.FactionTag ? Relation.Friendly : Relation.Enemy;
        return Relation.Unknown;
    }
    private int _teamBudget, _boneNameBudget, _mapBudget;
    private ulong _localState, _localPawn;
    private long _nextLocalTeam;
    private TeamIdentity _localTeam;
    private readonly Dictionary<uint, string> _nameCache = new();
    private int _nameLayout = -1;
    private readonly Dictionary<ulong,int> _factionProbes = new();
    private readonly Dictionary<ulong, string> _factionNames = new();
    // Supplied reference revisions; each candidate must resolve None and an exact faction tag.
    private static readonly ulong[] FactionPools = [O.GNames, 0xCE668C0, 0xD1A9B68];
    private static string CanonicalFactionName(string name)
    {
        if (string.Equals(name, "Meta.Alignment.Faction.Alpha", StringComparison.OrdinalIgnoreCase)) return "Meta.Alignment.Faction.Alpha";
        if (string.Equals(name, "Meta.Alignment.Faction.Bravo", StringComparison.OrdinalIgnoreCase)) return "Meta.Alignment.Faction.Bravo";
        if (string.Equals(name, "Meta.Alignment.Faction.Charlie", StringComparison.OrdinalIgnoreCase)) return "Meta.Alignment.Faction.Charlie";
        return "";
    }
    private static bool IsFactionName(string name) => CanonicalFactionName(name).Length != 0;
    private string NumericFactionName(ulong component)
    {
        if (component == 0) return "";
        ulong actor = Ptr(component + O.FactionComponentFaction);
        ulong data = actor != 0 ? Ptr(actor + O.FactionData) : 0;
        // Zero is a valid ID only after a successful read; failed reads never mean Lonestar.
        if (data == 0 || !Read(data + O.GenericTeamId, out byte id)) return "";
        return id switch { 0 => "Meta.Alignment.Faction.Alpha", 1 => "Meta.Alignment.Faction.Bravo",
            2 => "Meta.Alignment.Faction.Charlie", _ => "" };
    }
    private string FactionName(ulong tag)
    {
        if (tag == 0) return "";
        if (_factionNames.TryGetValue(tag, out var cached)) return cached;
        string name = Name((uint)tag);
        if (!IsFactionName(name))
        {
            // One bounded candidate per team refresh; never scan memory or guess labels from team relation.
            int candidate = _factionProbes.GetValueOrDefault(tag);
            if (_factionProbes.Count >= 128) _factionProbes.Clear();
            _factionProbes[tag] = (candidate + 1) % (FactionPools.Length * FactionNameLayouts.Length);
            int layout = candidate % FactionNameLayouts.Length;
            ulong pool = FactionPools[candidate / FactionNameLayouts.Length];
            name = DecodeName(0, layout, pool, faction: true) == "None" ? DecodeName((uint)tag, layout, pool, faction: true) : "";
        }
        if (!IsFactionName(name)) return "";
        if (_factionNames.Count >= 128) _factionNames.Clear();
        return _factionNames[tag] = CanonicalFactionName(name);
    }
    private static readonly (ulong Table, ulong Stride, ulong Header, ulong Data, int Shift)[] NameLayouts =
        [(0x10,8,8,0xC,6), (0x10,8,8,0xC,1), (0x10,2,0,2,6), (0,1,0,2,6)]; 
    private static readonly (ulong Table, ulong Stride, ulong Header, ulong Data, int Shift)[] FactionNameLayouts =
        [..NameLayouts, (0x20,8,8,0xC,6), (0x20,8,8,0xC,1), (0x20,2,0,2,6)];
    private readonly Dictionary<ulong, BoneMap> _maps = new();
    private readonly Dictionary<ulong, long> _mapRetry = new();

    private void ResetVisualCaches()
    {
        _localState = _localPawn = 0;
        _nextLocalTeam = 0;
        _localTeam = default;
        _nameCache.Clear();
        _nameLayout = -1;
        _factionProbes.Clear();
        _factionNames.Clear();
        _poseDiagnostics.Clear();
        _maps.Clear();
        _boneDiscovery.Clear();
        _bodyMeshes.Clear();
        _mapRetry.Clear();
    }
    private TeamIdentity LocalTeam(ulong pawn, ulong controller)
    {
        ulong state = Ptr(pawn+O.PawnPlayerState);
        if (state == 0) state = Ptr(controller+O.ControllerPlayerState);
        if (state == 0) { _localTeam = default; _localState = 0; return default; }
        if (state != _localState || pawn != _localPawn || _now >= _nextLocalTeam)
        {
            _localState = state;
            _localPawn = pawn;
            _nextLocalTeam = _now + 1000;
            scheduleRead?.Invoke(_nextLocalTeam);
            try { _localTeam = ReadTeam(state); }
            finally { scheduleRead?.Invoke(0); }
        }
        return _localTeam;
    }
    private TeamIdentity ReadTeam(ulong state)
    {
        Squad squad = default;
        ulong squadPointer = Ptr(state + O.SquadComponent);
        // Read all 128 bits together; a partial read must not establish a squad match.
        if (squadPointer != 0 && Read(squadPointer + O.SquadId, out Squad fullSquad))
            squad = fullSquad;
        ulong component = Ptr(state + O.PlayerStateFactionComponent);
        ulong rawTag = 0;
        if (component != 0 && Read(component + O.FactionComponentTag, out ulong tag))
            rawTag = tag;
        // Name resolution is display-only and cannot change team classification.
        string name = NumericFactionName(component);
        if (name.Length == 0) name = FactionName(rawTag);
        return new(rawTag, squad) { FactionNameTag = name };
    }
    private string DecodeName(uint id, int layoutIndex, ulong pool = O.GNames, bool faction = false)
    {
        if ((id >> 16) > 8191) return "";
        var layout = (faction ? FactionNameLayouts : NameLayouts)[layoutIndex];
        ulong block = Ptr(moduleBase + pool + layout.Table + (id >> 16) * 8UL);
        if (block == 0) return "";
        ulong entry = block + (id & 0xFFFF) * layout.Stride;
        if (!Read(entry + layout.Header, out ushort header)) return "";
        int length = header >> layout.Shift;
        if (layout.Header == 8 && layout.Data == 0xC && Read(entry + 0xA, out byte kind) && kind == 2) length = 22;
        if (length <= 0 || length > 512) return "";
        bool wide = (header & 1) != 0;
        byte[] bytes = new byte[length * (wide ? 2 : 1)];
        MarkReadOperation();
        if (!read(entry + layout.Data, bytes)) return "";
        string name = (wide ? Encoding.Unicode : Encoding.UTF8).GetString(bytes);
        return name.Any(char.IsControl) || name.Contains('\uFFFD') ? "" : name;
    }
    private string Name(uint id)
    {
        if (_nameCache.TryGetValue(id, out string value)) return value;
        // Select a pool layout using UE's known entry zero, rather than accepting
        // the first printable string from an unrelated address for each name.
        if (_nameLayout < 0)
            for (int i = 0; i < NameLayouts.Length; i++)
                if (DecodeName(0, i) == "None") { _nameLayout = i; break; }
        if (_nameLayout < 0) return "";
        string name = DecodeName(id, _nameLayout);
        if (name.Length == 0) return "";
        if (_nameCache.Count >= 16384) _nameCache.Clear();
        _nameCache[id] = name;
        return name;
    }

    public sealed record SkeletonPose(Vec[] Points, bool[] Valid)
    {
        public static readonly SkeletonPose Empty = new([],[]);
        public bool HasHead => Valid.Length > 0 && Valid[0];
    }
    // Slots follow the old project's semantic mapping, not raw game bone indexes.
    public static readonly (int A, int B)[] BoneLinks = [(0,1),(1,2),(2,3),(3,4),(4,5),
        (5,6),(6,7),(7,8),(2,10),(10,11),(11,12),
        (5,14),(14,15),(15,16),(2,18),(18,19),(19,20)];
    private static readonly int[] FallbackBones = [9,7,6,4,2,1,145,146,147,148,10,11,12,22,122,123,124,125,64,65,66,76];
    private static readonly string[][] Aliases = [
        ["head","head_01","head_m","ik_head","b_head","head_end"], ["neck_01","neck01","neck"], ["spine_05","spine_04","spine_03"],
        ["spine_03","spine03","spine_3"], ["spine_01","spine01","spine_1"], ["pelvis","hips","hip","pelvis_01"],
        ["thigh_l","thighleft","l_thigh","l_hip","hip_l"], ["calf_l","calfleft","l_calf","lowerleg_l","leftleg"], ["foot_l","footleft","l_foot","ankle_l"], [],
        ["clavicle_l","clavicleleft"], ["upperarm_l","upper_arm_l","l_upperarm","upperarm_l_01"], ["lowerarm_l","forearm_l","l_forearm","l_lowerarm","lowerarm_l_01"], [],
        ["thigh_r","thighright","r_thigh","r_hip","hip_r"], ["calf_r","calfright","r_calf","lowerleg_r","rightleg"], ["foot_r","footright","r_foot","ankle_r"], [],
        ["clavicle_r","clavicleright"], ["upperarm_r","upper_arm_r","r_upperarm","upperarm_r_01"], ["lowerarm_r","forearm_r","r_forearm","r_lowerarm","lowerarm_r_01"], []];
    [StructLayout(LayoutKind.Sequential)]
    private struct BoneInfo { public uint Name, Number; public int Parent; }
    private sealed class BoneMap(BoneInfo[] infos)
    {
        public readonly BoneInfo[] Infos = infos;
        public readonly int[] Selected = Enumerable.Repeat(-1,22).ToArray();
        public readonly int[] Priority = Enumerable.Repeat(int.MaxValue,22).ToArray();
        public int Cursor, ResolvedCount;
        public readonly bool[] Resolved = new bool[infos.Length];
        public long RetryAt, NextValidation;
        public ulong SourceData, Header;
        public bool Complete => ResolvedCount >= Infos.Length;
        public bool Usable => Selected[0]>=0 && Selected[1]>=0 && Selected[2]>=0 && Selected[5]>=0;
    }
    private BoneMap Map(ulong asset)
    {
        if (asset == 0) return null;
        if (_maps.TryGetValue(asset,out var cached) && cached.Complete && !cached.Usable && _now>=cached.RetryAt)
            _maps.Remove(asset);
        // Immutable in normal play, but asset addresses/hierarchy data can be reused.
        // Validate one due mapping per cycle without repeatedly discarding good mappings.
        if (_maps.TryGetValue(asset, out cached) && _now >= cached.NextValidation && _mapBudget > 0)
        {
            _mapBudget--;
            cached.NextValidation = _now + 1000; // Failed checks retry, without poisoning the old mapping.
            if (Read(cached.Header, out Arr current) && Pointer(current.Data)
                && current.Count >= 16 && current.Count <= 1024 && current.Capacity >= current.Count)
            {
                var infos = new BoneInfo[current.Count];
                MarkReadOperation();
                if (read(current.Data, MemoryMarshal.AsBytes(infos.AsSpan()))
                    && infos.Select((info, index) => info.Parent < index && info.Parent >= -1).All(valid => valid))
                {
                    if (current.Data != cached.SourceData || !infos.SequenceEqual(cached.Infos))
                        _maps[asset] = cached = new BoneMap(infos) { Header = cached.Header, SourceData = current.Data, RetryAt = _now + 10000 };
                    cached.NextValidation = _now + 5000;
                }
            }
        }
        if (!_maps.TryGetValue(asset,out var map))
        {
            if (_mapRetry.TryGetValue(asset,out long nextAttempt) && _now<nextAttempt) return null;
            if (_mapBudget <= 0) return null;
            _mapBudget--;
            if (_mapRetry.Count>=256) _mapRetry.Clear();
            _mapRetry[asset]=_now+100;
            if (!DiscoverBoneInfo(asset, out ulong header, out Arr array, out BoneInfo[] infos)) return null;
            if (_maps.Count>=256) _maps.Clear();
            _maps[asset] = map = new BoneMap(infos) { Header = header, RetryAt = _now+10000, NextValidation = _now+5000, SourceData = array.Data };
            _mapRetry.Remove(asset);
        }
        // Resolve a bounded number per cycle, caching by mesh asset rather than player.
        while (!map.Complete && _boneNameBudget>0)
        {
            _boneNameBudget--;
            int i=map.Cursor++ % map.Infos.Length;
            if (map.Resolved[i]) continue;
            string resolvedName = Name(map.Infos[i].Name);
            if (resolvedName.Length == 0) continue; // Retry later without blocking other bones.
            map.Resolved[i] = true;
            map.ResolvedCount++;
            string name=resolvedName.ToLowerInvariant().Replace('-','_').Replace(' ','_');
            for (int slot=0;slot<Aliases.Length;slot++)
            {
                int priority=System.Array.IndexOf(Aliases[slot],name);
                if (priority < 0 && !IsHelperBone(name))
                    for (int alias=0;alias<Aliases[slot].Length;alias++)
                        if (name.EndsWith("_"+Aliases[slot][alias], StringComparison.Ordinal)) { priority=100+alias; break; }
                if (priority>=0 && priority<map.Priority[slot]) { map.Selected[slot]=i; map.Priority[slot]=priority; }
            }
        }
        return map.Usable ? map : null;
    }
    private SkeletonPose ReadSkeleton(ulong pawn, Vec position, bool full, bool boundsOnly, long nextBody,
        out ulong mesh, out ulong asset)
    {
        asset = 0;
        mesh=SelectBodyMesh(Ptr(pawn+O.MoverMesh));
        if (mesh==0) return SkeletonPose.Empty;
        asset=Ptr(mesh+O.SkinnedAsset);
        if (asset==0) asset=Ptr(mesh+O.SkinnedAssetSecondary);
        BoneMap map;
        scheduleRead?.Invoke(long.MaxValue);
        try { map=Map(asset); }
        finally { scheduleRead?.Invoke(nextBody); }
        // Keep the established component-space fallback available while asset
        // mapping is pending/unavailable. Local-space poses still require a map.
        Transform component=default;
        bool componentValid=false;
        foreach (ulong offset in new ulong[] { O.MeshComponentToWorld,0x210,0x1E0 })
            if (Read(mesh+offset,out component) && component.Valid() && component.Translation.Distance(position)<500)
            { componentValid=true; break; }
        if (!componentValid) return SkeletonPose.Empty;
        foreach (ulong offset in PoseOffsets(mesh))
        {
            if (offset==0xA08 && map==null) continue;
            if (!Read(mesh+offset,out Arr array) || !Pointer(array.Data)) continue;
            if (!ValidPoseArray(array)) continue;
            if (map==null && array.Count<149) continue;
            var points=new Vec[22]; var valid=new bool[22];
            var selected=map==null ? FallbackBones : map.Selected;
            PrefetchSelectedBones(array,selected,map,offset,full,boundsOnly);
            var transforms=new Dictionary<int,Transform>();
            bool Get(int index,out Transform transform)
            {
                if (transforms.TryGetValue(index,out transform)) return true;
                if (index<0 || index>=array.Count || !Read(array.Data+(ulong)index*O.FTransformSize,out transform) || !transform.Valid()) return false;
                transforms[index]=transform; return true;
            }
            for (int slot=0;slot<22;slot++)
            {
                // Preserve semantic slot IDs, but never acquire the removed endpoints.
                if (slot is 9 or 13 or 17 or 21) continue;
                bool core = slot is 0 or 1 or 2 or 5;
                bool foot = slot is 8 or 16;
                if (!core && (!full || (boundsOnly && !foot))) continue;
                scheduleRead?.Invoke(nextBody);
                int index=selected[slot];
                if (!Get(index,out Transform transform)) continue;
                Vec point=transform.Translation;
                bool good=true;
                if (offset==0xA08)
                {
                    if (map==null) continue;
                    if (index>=map.Infos.Length) continue;
                    int parent=map.Infos[index].Parent, depth=0;
                    while (parent>=0)
                    {
                        if (++depth>128 || parent>=map.Infos.Length || !Get(parent,out Transform ancestor)) { good=false; break; }
                        point=ancestor.Apply(point); parent=map.Infos[parent].Parent;
                    }
                }
                point=component.Apply(point);
                if (!good || !point.Valid() || point.Distance(position)>400) continue;
                points[slot]=point; valid[slot]=true;
            }
            scheduleRead?.Invoke(nextBody);
            double spine=points[0].Distance(points[5]);
            if (valid[0] && valid[1] && valid[2] && valid[5] && spine>=15 && spine<=200)
            {
                // Reject a buffer that flipped/reallocated while its bones were read.
                if (!Read(mesh+offset, out Arr after) || after.Data != array.Data || after.Count != array.Count) continue;
                if (offset is 0x610 or 0x620 && Read(mesh+0x630, out int editing)
                    && editing is 0 or 1 && offset != 0x610UL + (ulong)(1-editing)*0x10) continue;
                RecordPoseSource(mesh,asset,offset,array.Count,map,valid.Count(v=>v));
                return new(points,valid);
            }
        }
        RecordPoseSource(mesh,asset,0,0,map,0);
        return SkeletonPose.Empty;
    }
}

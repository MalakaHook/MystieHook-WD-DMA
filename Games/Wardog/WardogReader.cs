using System.Numerics;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using O = MystieHook.Games.Wardog.WardogOffsets;

namespace MystieHook.Games.Wardog;

// Core reading path adapted from legacy Reader.h. See ThirdParty/Wardogs-LICENSE.txt.
public sealed partial class WardogReader(ulong moduleBase, WardogReader.ReadMemory read, WardogReader.ReadMemory readFresh = null,
    Func<long> clockMilliseconds = null, Action<long> scheduleRead = null,
    Action<IEnumerable<WardogFrameMemory.Range>> prefetch = null,
    Func<WardogReader.PredictionSpeedSample> predictionSpeed = null,
    Action<IReadOnlyCollection<WardogReadBatch.Request>> verifyScatter = null,
    Func<ulong,int,long> readSampleStarted = null,
    bool optimizedReads = WardogReadOptimization.Enabled,
    Action<WardogReadOperation> readOperation = null)
{
    public delegate bool ReadMemory(ulong address, Span<byte> destination);
    internal static long MonotonicMilliseconds => (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));
    private sealed class Metadata
    {
        public string Label = "Player";
        public long NextLabel;
        public TeamIdentity Team;
        public long NextTeam, TeamReadAt;
        public ulong Class;
        public int Health = -1;
        public bool Dead, Downed;
        public long NextHealth;
        public string CurrentItem = "";
        public long NextItem;
        public SkeletonPose Body = SkeletonPose.Empty;
        public ulong Mesh, Asset;
        public long BodyReadAt;
        public Vec BodyPosition;
        public BoneMap BodyMap;
        public int BodyMode;
        public bool WasOutsideRange;
    }
    private readonly Dictionary<(ulong State, ulong Pawn), Metadata> _metadata = new();
    private ulong _world;
    private int _labelBudget;
    private long _now;
    private ulong _playerClass;
    private readonly Dictionary<ulong, bool> _classes = new();
    private bool _focusedPlayerReads;
    public enum Relation { Unknown, Friendly, Enemy }
    [StructLayout(LayoutKind.Sequential)]
    public struct Vec
    {
        public double X, Y, Z;
        public bool Valid(double limit = 1e8) => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z)
            && Math.Abs(X) < limit && Math.Abs(Y) < limit && Math.Abs(Z) < limit;
        public double Distance(Vec b) => Math.Sqrt((X-b.X)*(X-b.X)+(Y-b.Y)*(Y-b.Y)+(Z-b.Z)*(Z-b.Z));
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Arr { public ulong Data; public int Count, Capacity; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Transform
    {
        public double X, Y, Z, W;
        public Vec Translation;
        public double Pad;
        public Vec Scale;
        public double Pad2;
        public bool Valid() => double.IsFinite(X*X+Y*Y+Z*Z+W*W) && X*X+Y*Y+Z*Z+W*W is > .25 and < 4
            && Translation.Valid() && Scale.Valid(100) && Scale.X > .001 && Scale.Y > .001 && Scale.Z > .001;
        public Vec Apply(Vec point)
        {
            double norm = Math.Sqrt(X*X+Y*Y+Z*Z+W*W);
            double x=X/norm, y=Y/norm, z=Z/norm, w=W/norm;
            double px=point.X*Scale.X, py=point.Y*Scale.Y, pz=point.Z*Scale.Z;
            double tx=2*(y*pz-z*py), ty=2*(z*px-x*pz), tz=2*(x*py-y*px);
            return new Vec { X=px+w*tx+y*tz-z*ty+Translation.X,
                Y=py+w*ty+z*tx-x*tz+Translation.Y, Z=pz+w*tz+x*ty-y*tx+Translation.Z };
        }
    }
    public readonly record struct Camera(Vec Location, Vec Rotation, float Fov);
    public sealed record Player(ulong Address, string Name, Vec Position, int Health)
    {
        public ulong SourceState { get; init; }
        // Root-component body facing, not independently replicated head/aim rotation.
        public double? FacingYaw { get; init; }
        public ulong SourceMesh { get; init; }
        public ulong SourceAsset { get; init; }
        public Vec? PredictionVelocity { get; init; }
        public long PoseSampleStarted { get; init; }
        public string CurrentItem { get; init; } = "";
        public string Faction { get; init; } = "Unknown";
        public bool Dead { get; init; }
        public bool Downed { get; init; }
        public Relation TeamRelation { get; init; }
        public SkeletonPose Pose { get; init; } = SkeletonPose.Empty;
        public SkeletonPose HeadPose { get; init; } = SkeletonPose.Empty;
    }
    public sealed record Frame(string Status, bool Valid, Camera Camera, Vec LocalPosition, Player[] Players)
    {
        public Player[] RadarPlayers { get; init; } = [];
        public Player[] WebRadarPlayers { get; init; } = [];
        public ulong WorldAddress { get; init; }
        public ulong LocalPawn { get; init; }
        public string LocalRcsWeapon {get;init;}="";
        public string LocalFaction {get;init;}="Unknown";
        public float LocalBulletSpeed { get; init; }
        public long BulletSpeedSampleStarted { get; init; }
        public bool BulletSpeedCached {get;init;}
        public string PredictionSpeedReadStatus { get; init; } = "Waiting for live bullet speed";
        public string PredictionSpeedBlockReason => !float.IsFinite(LocalBulletSpeed) || LocalBulletSpeed is <2000 or >200000
            ? PredictionSpeedReadStatus
            : BulletSpeedSampleStarted == 0 ? "Bullet-speed sample timestamp unavailable"
            : Stopwatch.GetElapsedTime(BulletSpeedSampleStarted).TotalMilliseconds < 0 ? "Bullet-speed sample timestamp is in the future"
            : !HasPredictionSpeed ? BulletSpeedCached ? "Player snapshot is stale for cached weapon speed" : "Live bullet-speed sample is older than 150 ms"
            : BulletSpeedCached ? "Cached weapon bullet speed ready" : "Live bullet speed ready";
        public bool HasPredictionSpeed => float.IsFinite(LocalBulletSpeed) && LocalBulletSpeed is >=2000 and <=200000
            && BulletSpeedSampleStarted != 0 && Stopwatch.GetElapsedTime(BulletSpeedSampleStarted).TotalMilliseconds>=0
            &&(BulletSpeedCached?IsFresh:Stopwatch.GetElapsedTime(BulletSpeedSampleStarted).TotalMilliseconds<=150);
        public ulong LocalController { get; init; }
        public int DetectedMap { get; init; } = -1;
        public long MapSampleStarted { get; init; }
        public MapIdentityDebug MapDebug { get; init; }
        public double DmaWaitMilliseconds { get; init; }
        public double DmaMilliseconds { get; init; }
        public double LongestTransferMilliseconds { get; init; }
        public string LongestTransferKind { get; init; } = "none";
        public int DmaOperations { get; init; }
        public int PoseFailures { get; init; }
        public int PoseFallbacks { get; init; }
        public long SampleStarted { get; init; }
        public long CompletedAt { get; init; }
        public long DisplayUntil { get; init; }
        public bool IsDisplayFresh => DisplayUntil == 0 ? IsFresh : Valid && Stopwatch.GetTimestamp() <= DisplayUntil;
        public double ReadMilliseconds { get; init; }
        public double RefreshHz { get; init; }
        public int DirectReads { get; init; }
        public int BatchedReads { get; init; }
        public bool IsFresh => Valid && SampleStarted != 0 && Stopwatch.GetElapsedTime(SampleStarted).TotalMilliseconds < 250;
        public static Frame Empty(string status) => new(status, false, default, default, []);
    }
    public static bool Pointer(ulong p) => p >= 0x10000 && p < 0x0000800000000000;
    private void MarkReadOperation([CallerMemberName] string caller = "") => readOperation?.Invoke(WardogReadOperations.FromCaller(caller));
    private bool Read<T>(ulong address, out T value, [CallerMemberName] string caller = "") where T : unmanaged
    {
        value = default;
        if (!Pointer(address)) return false;
        MarkReadOperation(caller);
        Span<T> one = stackalloc T[1];
        if (!read(address, MemoryMarshal.AsBytes(one))) return false;
        value = one[0];
        return true;
    }
    private ulong Ptr(ulong address, [CallerMemberName] string caller = "") => Read(address, out ulong p, caller) && Pointer(p) ? p : 0;
    private bool Array(ulong address, int max, out ulong[] pointers)
    {
        pointers = [];
        if (!Read(address, out Arr a) || a.Count < 0 || a.Count > max || a.Capacity < a.Count || a.Capacity > max*4) return false;
        if (a.Count == 0) return true;
        if (!Pointer(a.Data)) return false;
        pointers = new ulong[a.Count];
        MarkReadOperation();
        if (read(a.Data, MemoryMarshal.AsBytes(pointers.AsSpan()))) return true;
        pointers = [];
        return false;
    }
    private string Text(ulong address)
    {
        if (!Read(address, out Arr a) || !Pointer(a.Data) || a.Count <= 0 || a.Count > 128 || a.Capacity < a.Count) return "";
        var bytes = new byte[a.Count*2];
        MarkReadOperation();
        if (!read(a.Data, bytes)) return "";
        return Encoding.Unicode.GetString(bytes).Split('\0')[0];
    }
    private bool Position(ulong pawn, out Vec position)
        => Position(pawn, out position, out _, false);
    private bool Position(ulong pawn, out Vec position, out double? facingYaw, bool readFacing,Vec? facingOrigin=null)
    {
        position = default;
        facingYaw = null;
        ulong root = Ptr(pawn+O.ActorRootComponent);
        if (root == 0) return false;
        foreach (ulong offset in new ulong[] { 0x210, 0x1D0, 0x1E0 })
        {
            bool validTransform=Read(root+offset, out Transform t) && t.Valid();
            if (validTransform)
            {
                position = t.Translation;
                if (readFacing)
                {
                    var forward=t.Apply(new Vec {X=1});
                    double dx=forward.X-position.X,dy=forward.Y-position.Y;
                    if (dx*dx+dy*dy>1e-8) facingYaw=(Math.Atan2(dy,dx)*180/Math.PI+360)%360;
                }
                return true;
            }
        }
        bool valid=Read(root+O.SceneRelativeLocation, out position) && position.Valid();
        if (valid&&readFacing&&(facingOrigin==null||WardogReadRange.Contains(position,facingOrigin.Value))
            &&Read(root+O.SceneRelativeRotation,out Vec rotation)&&rotation.Valid(36001))
            facingYaw=(rotation.Y%360+360)%360;
        return valid;
    }
    private ulong Controller(ulong world)
    {
        ulong engine = Ptr(moduleBase+O.GEngine);
        foreach (ulong instance in new[] { Ptr(world+O.GameInstance), engine == 0 ? 0 : Ptr(engine+O.GameInstance), engine == 0 ? 0 : Ptr(engine+O.GameInstanceAlt) })
            if (instance != 0 && Array(instance+O.LocalPlayers, 16, out var locals))
                foreach (ulong local in locals)
                    if (Pointer(local) && Ptr(local+O.LocalPlayerController) is var controller && controller != 0) return controller;
        return 0;
    }
    private bool ReadCamera(ulong controller, out Camera camera)
    {
        camera = default;
        ulong manager = Ptr(controller+O.CameraManager);
        if (manager == 0) return false;
        Span<byte> bytes = stackalloc byte[0x60];
        foreach (ulong offset in new ulong[] { O.CurrentCameraCache, 0x1E40, 0x350, 0xC40 })
        {
            MarkReadOperation();
            if (!read(manager+offset, bytes)) continue;
            Vec location = MemoryMarshal.Read<Vec>(bytes[0x10..]);
            Vec rotation = MemoryMarshal.Read<Vec>(bytes[0x28..]);
            float fov = MemoryMarshal.Read<float>(bytes[0x40..]);
            if (location.Valid() && rotation.Valid(361) && Math.Abs(rotation.X) <= 90 && float.IsFinite(fov) && fov > 1 && fov < 160)
            { camera = new(location, rotation, fov); return true; }
        }
        return false;
    }
    private ulong PlayerClass()
    {
        ulong chunks=Ptr(moduleBase+O.GObjects);
        if(chunks==0)return 0;
        ulong chunk=Ptr(chunks+O.PlayerBaseClassIndex/O.ObjectsPerChunk*8)&~0xFUL;
        if(!Pointer(chunk))return 0;
        // Latest dumper SafeMode validated the object pointer at entry +0x10.
        return Ptr(chunk+O.PlayerBaseClassIndex%O.ObjectsPerChunk*O.ObjectItemSize+O.PlayerClassObjectPointer);
    }
    private bool IsPlayerType(ulong type,ulong target)
    {
        if(type==0)return false;
        if(_classes.TryGetValue(type,out bool cached))return cached;
        ulong original=type;
        scheduleRead?.Invoke(long.MaxValue);
        try
        {
            for(int i=0;i<32 && type!=0;i++,type=Ptr(type+O.ClassSuper))
                if(type==target)
                {
                    if(_classes.Count>=4096)_classes.Clear();
                    _classes[original]=true;
                    return true;
                }
            return false;
        }
        finally {scheduleRead?.Invoke(0);}
    }
    private int Health(ulong pawn, out bool depleted)
    {
        depleted = false;
        ulong vitality = Ptr(pawn+O.VitalityComponent);
        // Current supplied chain: vitality -> attribute -> double CurrentValue.
        if (vitality != 0 && Read(vitality + O.VitalityMax, out float maximum)
            && float.IsFinite(maximum) && maximum > 0 && maximum < 1e6)
        {
            ulong attribute = Ptr(vitality + O.CurrentHealthHandle);
            if (attribute != 0
                && Read(attribute + O.AttributeValue, out double current)
                && double.IsFinite(current) && current >= 0 && current <= maximum * 2)
            {
                depleted = current == 0;
                return (int)Math.Clamp(current / maximum * 100, 0, 100);
            }
        }
        // BaseHealth and inline pawn state are not substitutes for a failed live read.
        return -1;
    }
    private void LifeState(ulong pawn, ulong state, Metadata info, bool depleted)
    {
        info.Dead = info.Downed = false;
        foreach (ulong address in new[] { pawn + 0x7FC, state + 0x61C, state + 0x628 })
        {
            if (!Read(address, out uint id) || id == 0) continue;
            string tag = Name(id);
            if (tag == "State.Player.Vitality.Dead") { info.Dead = true; info.Health = 0; return; }
            if (tag == "State.Player.Vitality.Critical") { info.Downed = true; return; }
            if (tag == "State.Player.Vitality.Alive") break;
        }
        // A confirmed Critical tag stays downed; otherwise exact live zero health
        // counts as dead, even when an Alive tag has not caught up yet.
        // Failed reads and rounded display percentages do not authorize hiding.
        info.Dead = depleted;
    }
    public Frame Capture(WardogVisualSettings settings = null, float aspectRatio = 16f / 9, bool visualsEnabled = true, CameraFrame independentCamera = null, WardogRadarSettings radar = null, bool detectMap = false, bool prediction = false, bool rcs = false)
    {
        long captureStarted = Stopwatch.GetTimestamp();
        ulong world = Ptr(moduleBase+O.GWorld);
        if (world != _world)
        {
            _world = world;
            _metadata.Clear();
            _classes.Clear();
            _playerClass = 0;

            ResetVisualCaches();
            _displayNames.Clear();
            _displayPoolValidated = false;
            _detectedMap = -1; _nextMapRead = _mapSampleStarted = 0; _mapDebug=null;
        }
        _now = clockMilliseconds?.Invoke() ?? MonotonicMilliseconds;
        // Spread discovery and expiry over frames instead of pausing all positions
        // for a full roster of player names at once.
        _labelBudget = 2;

        _teamBudget = 4;
        _boneNameBudget = 12;
        _mapBudget = 1;
        _bodyBudget = 1;
        if (world == 0) return Frame.Empty("Waiting for Wardog world");
        ulong controller = Controller(world);
        if (controller == 0) return Frame.Empty("Waiting for local controller");
        ulong pawnOffset=O.ControllerPawnCurrent;
        ulong pawn = Ptr(controller+pawnOffset);
        if (pawn == 0) { pawnOffset=O.ControllerPawn; pawn = Ptr(controller+pawnOffset); }
        if (pawn == 0) { pawnOffset=O.ControllerPawnAlt; pawn = Ptr(controller+pawnOffset); }
        if (pawn == 0 || !Position(pawn, out Vec local)) return Frame.Empty("Local player position unavailable");
        bool needsTeam = settings != null && visualsEnabled && settings.HasVisuals && (settings.ShowFaction || !settings.ShowTeammates || settings.ShowBoxes
            || (settings.ShowBones && settings.BoneColor == null) || (settings.ShowHeadDot && settings.HeadDotColor == null));
        needsTeam |= radar?.Enabled == true;
        TeamIdentity localTeam = needsTeam ? LocalTeam(pawn, controller) : default;
        ulong state = Ptr(world+O.WorldGameState);
        if (state == 0 || !Array(state+O.GameStatePlayerArray, 2048, out var states)) return Frame.Empty("Player list unavailable");
        UpdateMapIdentity(world, state, detectMap);
        if (_playerClass == 0)
        {
            scheduleRead?.Invoke(long.MaxValue);
            try { _playerClass = PlayerClass(); }
            finally { scheduleRead?.Invoke(0); }
        }
        ulong playerClass = _playerClass;
        if (playerClass == 0) return Frame.Empty("Player class unavailable; check game build");
        _focusedPlayerReads = optimizedReads || settings?.ShowTeammates == false;
        if (prefetch != null)
            PrefetchPlayerLinks(states);
        if (prefetch != null && !_focusedPlayerReads)
        {
            var actors = states.Where(Pointer).Select(ps => (State: ps, Actor: Ptr(ps+O.PlayerStatePawn)))
                .Where(p => Pointer(p.Actor)&&Position(p.Actor,out var position)&&WardogReadRange.Contains(position,local)).ToArray();
            prefetch(actors.Select(p => new WardogFrameMemory.Range(p.State, 0x600)));
            prefetch(actors.Select(p => new WardogFrameMemory.Range(p.Actor, 0x900)));
            var ranges = new List<WardogFrameMemory.Range>();
            ulong manager = Ptr(controller + O.CameraManager);
            if (Pointer(manager)) ranges.Add(new(manager + O.CurrentCameraCache, 0x60));
            foreach (var actor in actors)
            {
                ranges.Add(new(Ptr(actor.Actor+O.ActorRootComponent), 0x280));
                ranges.Add(new(Ptr(actor.Actor+O.VitalityComponent), 0x148));
                ranges.Add(new(Ptr(actor.State+O.PlayerStateFactionComponent), 0x140));
                if (settings?.ShowBones == true || settings?.ShowBoxes == true || settings?.ShowHeadDot == true)
                    ranges.Add(new(Ptr(actor.Actor+O.MoverMesh), 0xA40));
            }
            prefetch(ranges);
            ranges.Clear();
            foreach (var actor in actors)
            {
                if (!(settings?.ShowBones == true || settings?.ShowBoxes == true || settings?.ShowHeadDot == true)) break;
                ulong mesh = Ptr(actor.Actor+O.MoverMesh);
                if (!Pointer(mesh) || !Position(actor.Actor, out var position) || position.Distance(local)/100 > settings.MaximumDistance) continue;
                // Keep our verified pose layout, not the reference's older +630 array interpretation.
                foreach (ulong offset in new ulong[] { 0x610, 0x620, 0xA18, 0xA08 })
                    if (Read(mesh+offset, out Arr pose) && Pointer(pose.Data) && pose.Count is >= 10 and <= 1024 && pose.Capacity >= pose.Count)
                        ranges.Add(new(pose.Data, pose.Count * 96));
            }
            prefetch(ranges);
        }
        Camera camera;
        bool cameraValid;
        if (independentCamera is { IsFresh: true } && independentCamera.World == world && independentCamera.Controller == controller)
        { camera = independentCamera.Camera; cameraValid = true; }
        else cameraValid = ReadCamera(controller, out camera);
        if (!cameraValid) return Frame.Empty("Camera unavailable");
        var players = new List<Player>();
        var radarPlayers = new List<Player>();
        var webPlayers = new List<Player>();
        int poseFailures = 0, poseFallbacks = 0;
        var seen = new HashSet<ulong>();
        var identities = new HashSet<(ulong State, ulong Pawn)>();
        foreach (ulong ps in states)
        {
            if (!Pointer(ps)) continue;
            ulong actor = Ptr(ps+O.PlayerStatePawn);
            if (actor == 0 || actor == pawn || !seen.Add(actor)) continue;
            ulong type = Ptr(actor+O.UObjectClass);
            if (!IsPlayerType(type, playerClass)) continue;
            var key = (ps, actor);
            identities.Add(key);
            if (!_metadata.TryGetValue(key, out var info) || info.Class != type)
                _metadata[key] = info = new Metadata { Class = type, NextLabel = _now, NextTeam = _now, NextHealth = _now };
            if (!Position(actor, out Vec position, out double? facingYaw, radar?.WebSharing == true || (radar?.Enabled==true&&radar.LocalMap&&radar.Players),facingOrigin:local)) { info.Body = SkeletonPose.Empty; continue; }
            if(!WardogReadRange.Contains(position,local)){_metadata.Remove(key);continue;}
            if (radar?.Enabled == true && position.Distance(local)/100 <= radar.Range)
            {
                if (_now >= info.NextTeam && _teamBudget > 0)
                {
                    _teamBudget--; scheduleRead?.Invoke(_now+1000);
                    try { info.Team=ReadTeam(ps); } finally { scheduleRead?.Invoke(0); }
                    info.TeamReadAt=_now; info.NextTeam=_now+1000;
                }
                if (radar.Names && _now >= info.NextLabel && _labelBudget > 0)
                {
                    _labelBudget--; scheduleRead?.Invoke(long.MaxValue);
                    string label;
                    try { label=Text(ps+O.PlayerName); } finally { scheduleRead?.Invoke(0); }
                    bool valid=!string.IsNullOrWhiteSpace(label) && !label.Any(char.IsControl) && !label.Contains('\uFFFD');
                    if (valid) info.Label=label;
                    info.NextLabel=valid ? long.MaxValue : _now+1000;
                }
                if ((radar.WebSharing || radar.LocalMap || settings?.ShowDeadPlayers == false) && _now >= info.NextHealth)
                {
                    info.NextHealth=_now+50; scheduleRead?.Invoke(info.NextHealth);
                    try { info.Health=Health(actor,out bool depleted); LifeState(actor,ps,info,depleted); }
                    finally { scheduleRead?.Invoke(0); }
                }
                var team=CompareTeams(localTeam,_now-info.TeamReadAt<=3000 ? info.Team : default);
                if(radar.WebSharing && !info.Dead)
                {
                    webPlayers.Add(new(actor,info.Label,position,info.Health) {TeamRelation=team,SourceState=ps,FacingYaw=facingYaw});
                }
                if (radar.LocalMap ? radar.Players && !info.Dead && (radar.Teammates || team != Relation.Friendly)
                    : (settings?.ShowTeammates != false || team != Relation.Friendly) && (settings?.ShowDeadPlayers != false || !info.Dead))
                    radarPlayers.Add(new(actor,info.Label,position,info.Health) { TeamRelation=team, Dead=info.Dead, FacingYaw=facingYaw, Faction=_now-info.TeamReadAt<=3000?info.Team.DisplayName:"Unknown" });
            }
            if(settings!=null)
            {
                bool outside=position.Distance(local)/100>settings.MaximumDistance;
                if(outside)info.WasOutsideRange=true;
                else if(info.WasOutsideRange)
                {
                    info.WasOutsideRange=false;
                    info.Body=SkeletonPose.Empty;info.BodyReadAt=0;info.BodyMap=null;
                    // Forget this pawn's discovered body source; retain global name caches.
                    _bodyMeshes.Remove(Ptr(actor+O.MoverMesh));
                    if(_maps.TryGetValue(info.Asset,out var staleMap))staleMap.NextValidation=0;
                }
            }
            if (settings != null && (!visualsEnabled || !settings.HasVisuals
                || position.Distance(local) / 100 > settings.MaximumDistance
                || !cameraValid || !InView(camera, position, aspectRatio)))
            {
                info.Body = SkeletonPose.Empty;
                if (radar?.Enabled != true) info.NextHealth = 0;
                info.NextItem = 0;
                continue;
            }
            Relation relation = Relation.Unknown;
            if (settings != null)
            {
                if (needsTeam && _now >= info.NextTeam && _teamBudget > 0)
                {
                    _teamBudget--;
                    scheduleRead?.Invoke(_now + 1000);
                    try { info.Team = ReadTeam(ps); }
                    finally { scheduleRead?.Invoke(0); }
                    info.TeamReadAt = _now;
                    info.NextTeam = _now + 1000;
                }
                relation = CompareTeams(localTeam, _now-info.TeamReadAt <= 3000 ? info.Team : default);
                if (!settings.Allows(position, local, relation)) { info.Body = SkeletonPose.Empty; continue; }
            }
            bool needsLife = settings != null; // Dead players use a distinct name-only presentation even with health bars off.
            bool needsHealth = settings == null || settings.ShowHealth || needsLife;
            if (!needsHealth) { info.Health = -1; info.NextHealth = 0; }
            if (!needsLife) { info.Dead = info.Downed = false; }
            if (needsHealth && _now >= info.NextHealth)
            {
                info.NextHealth = _now + 50;
                scheduleRead?.Invoke(info.NextHealth);
                try { info.Health = Health(actor, out bool depleted); if (needsLife) LifeState(actor, ps, info, depleted); }
                finally { scheduleRead?.Invoke(0); }
            }
            if (settings != null && !settings.Allows(position, local, relation, info.Dead, info.Downed))
            { info.Body = SkeletonPose.Empty; continue; }
            if ((settings == null || settings.ShowName || (settings.ShowDeadPlayers && info.Dead)) && _now >= info.NextLabel && _labelBudget > 0)
            {
                _labelBudget--;
                scheduleRead?.Invoke(long.MaxValue);
                string name;
                try { name = Text(ps+O.PlayerName); }
                finally { scheduleRead?.Invoke(0); }
                bool valid = !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl) && !name.Contains('\uFFFD');
                if (valid) info.Label = name;
                info.NextLabel = valid ? long.MaxValue : _now + 1000;
            }
            SkeletonPose head = SkeletonPose.Empty;
            if (!info.Dead && !info.Downed && settings != null && (settings.ShowBones || settings.ShowHeadDot || settings.ShowName || settings.ShowFaction || settings.ShowHealth || settings.ShowBoxes))
            {
                // Always attempt fresh pose data. Retain a recent good pose only for a
                // brief failure with matching identity, mesh, asset, mapping and mode.
                bool needsBody = settings.ShowBones || settings.ShowBoxes || settings.ShowHealth;
                int mode = settings.ShowBones ? 3 : needsBody ? 2 : 1;
                ulong previousMesh = info.Mesh, previousAsset = info.Asset;
                SkeletonPose nextPose;
                scheduleRead?.Invoke(0);
                try
                {
                    nextPose = ReadSkeleton(actor, position, needsBody, !settings.ShowBones, 0,
                        out info.Mesh, out info.Asset);
                }
                finally { scheduleRead?.Invoke(0); }
                var mapping = _maps.GetValueOrDefault(info.Asset);
                if (nextPose.HasHead)
                {
                    info.Body = nextPose;
                    info.BodyReadAt = _now;
                    info.BodyPosition = position;
                    info.BodyMap = mapping;
                    info.BodyMode = mode;
                }
                else
                {
                    poseFailures++;
                    bool canRetain = info.Body.HasHead && _now - info.BodyReadAt <= 80
                        && _now >= info.BodyReadAt && previousMesh == info.Mesh && info.Mesh != 0
                        && previousAsset == info.Asset && info.Asset != 0 && ReferenceEquals(info.BodyMap, mapping)
                        && info.BodyMode == mode && position.Distance(info.BodyPosition) <= 25;
                    if (canRetain) poseFallbacks++;
                    else info.Body = SkeletonPose.Empty;
                }
                head = info.Body;
            }
            else { info.Body = SkeletonPose.Empty; }
            if (!info.Dead && !info.Downed && settings?.ShowCurrentItem == true)
            {
                if (_now >= info.NextItem)
                {
                    info.NextItem = _now + 100;
                    scheduleRead?.Invoke(info.NextItem);
                    try { info.CurrentItem = ReadCurrentItem(actor); }
                    finally { scheduleRead?.Invoke(0); }
                }
            }
            else { info.CurrentItem = ""; info.NextItem = 0; }
            players.Add(new(actor, info.Label, position, info.Health) { SourceState = ps, SourceMesh = info.Mesh, SourceAsset = info.Asset, TeamRelation = relation, Faction = _now - info.TeamReadAt <= 3000 ? info.Team.DisplayName : "Unknown", Pose = settings != null && (settings.ShowBones || settings.ShowBoxes || settings.ShowHealth) ? info.Body : SkeletonPose.Empty, HeadPose = head, PoseSampleStarted = head.HasHead ? captureStarted - (long)((_now-info.BodyReadAt)*Stopwatch.Frequency/1000.0) : 0, CurrentItem = info.CurrentItem, Dead = info.Dead, Downed = info.Downed });
        }
        foreach (var key in _metadata.Keys.Where(key => !identities.Contains(key)).ToArray()) _metadata.Remove(key);
        var predictionSpeed = ReadPredictionSpeed(world, controller, pawn, pawnOffset, prediction, captureStarted);
        string rcsWeapon="";
        if(rcs)
        {
            ulong inv=Ptr(pawn+O.PlayerInventory),held=inv==0?0:Ptr(inv+O.InventoryHeldItem);
            if(held!=0&&Read(held+O.ItemId,out uint itemId))
            {
                string tag=DisplayObjectName(itemId);
                Span<byte> check=stackalloc byte[8];var fresh=readFresh??read;
                readOperation?.Invoke(WardogReadOperation.RcsGuard);
                if(fresh(controller+pawnOffset,check)&&MemoryMarshal.Read<ulong>(check)==pawn
                    &&fresh(pawn+O.PlayerInventory,check)&&MemoryMarshal.Read<ulong>(check)==inv
                    &&fresh(inv+O.InventoryHeldItem,check)&&MemoryMarshal.Read<ulong>(check)==held
                    &&fresh(held+O.ItemId,check[..4])&&MemoryMarshal.Read<uint>(check)==itemId)
                    rcsWeapon=WardogWeaponTable.HeldItemLabel(tag);
            }
        }
        // Bypass the frame batch: a world switch during the scan must invalidate it.
        Span<byte> worldBytes = stackalloc byte[8];
        readOperation?.Invoke(WardogReadOperation.WorldProof);
        if (!(readFresh ?? read)(moduleBase+O.GWorld, worldBytes) || MemoryMarshal.Read<ulong>(worldBytes) != world)
            return Frame.Empty("World changed; refreshing");
        return new(cameraValid ? "Connected" : "Connected; camera unavailable", true, camera, local, players.ToArray()) { LocalFaction = localTeam.DisplayName, LocalRcsWeapon = rcsWeapon, LocalBulletSpeed = predictionSpeed.Speed, BulletSpeedSampleStarted = predictionSpeed.Stamp, BulletSpeedCached=predictionSpeed.Cached, PredictionSpeedReadStatus=predictionSpeed.Status, MapDebug = _mapDebug, DetectedMap = _detectedMap, MapSampleStarted = _mapSampleStarted, RadarPlayers = radarPlayers.ToArray(), WebRadarPlayers=webPlayers.ToArray(), WorldAddress = world, LocalPawn = pawn, LocalController = controller, PoseFailures = poseFailures, PoseFallbacks = poseFallbacks };
    }
    private string ReadCurrentItem(ulong pawn)
    {
        ulong inventory = Ptr(pawn + O.PlayerInventory);
        if (inventory == 0) return "";
        ulong held = Ptr(inventory + O.InventoryHeldItem);
        if (held == 0 && O.InventoryActiveItem != 0) held = Ptr(inventory + O.InventoryActiveItem);
        if (held == 0) return "";
        // ABHItem::ItemId is the gameplay identity, independent of its generic class.
        if (Read(held + O.ItemId, out uint itemId) && itemId != 0)
        {
            string tag = DisplayObjectName(itemId);
            if (tag.StartsWith("Id.Item.", StringComparison.OrdinalIgnoreCase))
                return WardogWeaponTable.HeldItemLabel(tag);
        }
        ulong type = Ptr(held + O.UObjectClass);
        if (type == 0 || !Read(type + O.ClassNameId, out uint id)) return "";
        // UE name-pool entries are immutable; only the selected item chain refreshes.
        scheduleRead?.Invoke(long.MaxValue);
        string label;
        try { label = DisplayObjectName(id); }
        finally { scheduleRead?.Invoke(_now + 100); }
        // Never turn a default object or unrelated engine configuration into an item label.
        if (label is "" or "None" || label.StartsWith("Default__", StringComparison.Ordinal)
            || !(label.StartsWith("BP_", StringComparison.Ordinal) || label.StartsWith("B_", StringComparison.Ordinal)
                || label.StartsWith("WD", StringComparison.Ordinal))) return "";
        foreach (string suffix in new[] { "_C", "_BP" })
            if (label.EndsWith(suffix, StringComparison.Ordinal)) label = label[..^suffix.Length];
        if (label.StartsWith("BP_", StringComparison.Ordinal)) label = label[3..];
        return label.Replace('_', ' ').Trim();
    }


    public static bool InView(Camera camera, Vec position, float aspectRatio)
    {
        if (!float.IsFinite(aspectRatio) || aspectRatio <= 0) return true;
        // Conservative player-sized bounds keep partially visible heads/limbs
        // readable at screen edges without requiring any bones for this check.
        float width = 1000 * aspectRatio, height = 1000;
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        int projected = 0;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
        {
            var corner = new Vec { X = position.X + x * 200, Y = position.Y + y * 200, Z = position.Z + z * 200 };
            if (!Project(camera, corner, width, height, out var point)) continue;
            projected++;
            left = Math.Min(left, point.X); right = Math.Max(right, point.X);
            top = Math.Min(top, point.Y); bottom = Math.Max(bottom, point.Y);
        }
        if (projected == 0) return false;
        if (projected < 8) return true; // Bounds cross the near plane: don't cull prematurely.
        return right >= 0 && bottom >= 0 && left <= width && top <= height;
    }

    public static bool Project(Camera c, Vec world, float width, float height, out Vector2 screen)
    {
        screen = default;
        if (!c.Location.Valid() || !c.Rotation.Valid(361) || !world.Valid() || !float.IsFinite(width) || !float.IsFinite(height)
            || width <= 0 || height <= 0 || !float.IsFinite(c.Fov) || c.Fov <= 1 || c.Fov >= 179) return false;
        double r = Math.PI/180, p = c.Rotation.X*r, yaw = c.Rotation.Y*r;
        double fx = Math.Cos(p)*Math.Cos(yaw), fy = Math.Cos(p)*Math.Sin(yaw), fz = Math.Sin(p);
        double dx = world.X-c.Location.X, dy = world.Y-c.Location.Y, dz = world.Z-c.Location.Z;
        double depth = dx*fx+dy*fy+dz*fz;
        if (depth <= 1) return false;
        double focal = width/(2*Math.Tan(c.Fov*r/2));
        double right = -dx*Math.Sin(yaw)+dy*Math.Cos(yaw);
        double up = -dx*fz*Math.Cos(yaw)-dy*fz*Math.Sin(yaw)+dz*Math.Cos(p);
        double roll = c.Rotation.Z*r;
        double x = width/2+(right*Math.Cos(roll)-up*Math.Sin(roll))*focal/depth;
        double y = height/2-(right*Math.Sin(roll)+up*Math.Cos(roll))*focal/depth;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
        screen = new((float)x, (float)y);
        return true;
    }
}


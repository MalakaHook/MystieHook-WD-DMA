using System.Diagnostics;
using System.Runtime.InteropServices;
using O = MystieHook.Games.Wardog.WardogOffsets;

namespace MystieHook.Games.Wardog;

// An independent reader instance owns these caches; it never shares mutable player caches.
public sealed partial class WardogReader
{
    public sealed record Vehicle(ulong Address, Vec Position, string Name)
    {
        public Relation TeamRelation { get; init; }
        public uint? ObjectNameId { get; init; }
        public string RawObjectName { get; init; } = "";
        // Name-pool IDs are runtime identities, not stable vehicle categories.
        public string DisplayLabel => string.IsNullOrWhiteSpace(Name) ? "Vehicle" : Name;
        public string IdentificationLabel => $"Instance 0x{Address:X}" + (ObjectNameId is uint id ? $" | NameID 0x{id:X8}" : " | NameID unavailable")
            + (string.IsNullOrWhiteSpace(RawObjectName) ? "" : $"\n{RawObjectName}");
    }
    public sealed record VehicleFrame(ulong World, Vehicle[] Vehicles, long SampleStarted)
    {
        public static readonly VehicleFrame Empty = new(0, [], 0);
        public bool IsFresh => SampleStarted != 0 && Stopwatch.GetElapsedTime(SampleStarted).TotalMilliseconds < 250;
    }
    private readonly Dictionary<ulong, ulong> _vehicleActors = new(), _foundVehicles = new();
    private readonly Dictionary<ulong, bool> _vehicleTypes = new();
    private readonly Dictionary<ulong, string> _vehicleLabels = new();
    private sealed record VehicleIdentity(ulong Type, uint? Id, string RawName, long NextCheck);
    private readonly Dictionary<ulong, VehicleIdentity> _vehicleIdentities = new();
    private sealed record VehicleMeshIdentity(ulong Type, ulong Component, ulong Asset, uint NameId, string Label, long ReadAt);
    private readonly Dictionary<ulong, VehicleMeshIdentity> _vehicleMeshes = new();
    private readonly Dictionary<ulong, long> _nextVehicleMeshRead = new();
    private readonly Dictionary<(ulong Asset, uint NameId), string> _vehicleMeshLabels = new();
    private readonly Dictionary<(ulong Asset, uint NameId), long> _vehicleMeshRetry = new();
    private readonly Dictionary<(ulong, string), bool> _namedTypes = new();
    private ulong _vehicleWorld, _baseVehicle, _modularVehicle;
    private ulong[] _scanLevels = [];
    private int _scanLevel, _actorCursor, _scanTotal;
    private Arr _scanActors;
    private bool _scanning, _scanFailed;
    private long _nextVehicleScan, _nextOccupantRoster;
    private ulong[] _occupantStates = [];
    private int _occupantCursor;
    private TeamIdentity _occupantLocalTeam;
    private readonly Dictionary<ulong, Occupant> _occupants = new();
    private sealed record Occupant(ulong Seat, bool Friendly, long ReadAt);

    private ulong ObjectAt(ulong index)
    {
        ulong chunks = Ptr(moduleBase + O.GObjects);
        ulong chunk = chunks == 0 ? 0 : Ptr(chunks + index / O.ObjectsPerChunk * 8) & ~0xFUL;
        if (chunk == 0) return 0;
        ulong item = chunk + index % O.ObjectsPerChunk * O.ObjectItemSize;
        ulong value = Ptr(item + O.ObjectItemObject);
        return value != 0 ? value : Ptr(item + 0x10);
    }
    private bool VehicleType(ulong type)
    {
        if (type == 0) return false;
        if (_vehicleTypes.TryGetValue(type, out bool known)) return known;
        ulong current = type;
        for (int i = 0; i < 32 && current != 0; i++)
        {
            if (current == _baseVehicle || current == _modularVehicle)
            { _vehicleTypes[type] = true; return true; }
            if (!Read(current + O.ClassSuper, out ulong parent)) return false;
            current = Pointer(parent) ? parent : 0;
        }
        if (current == 0) _vehicleTypes[type] = false;
        if (_vehicleTypes.Count > 4096) _vehicleTypes.Clear();
        return false;
    }
    private bool NamedObject(ulong obj, string expected)
    {
        ulong type = Ptr(obj + O.UObjectClass);
        if (type == 0) return false;
        var key = (type, expected);
        if (_namedTypes.TryGetValue(key, out bool value)) return value;
        for (int depth = 0; depth < 32 && type != 0; depth++, type = Ptr(type + O.ClassSuper))
            if (Read(type + O.ClassNameId, out uint id) && DisplayObjectName(id) == expected)
            {
                if (_namedTypes.Count > 1024) _namedTypes.Clear();
                _namedTypes[key] = true;
                return true;
            }
        return false; // A failed name read must be retried.
    }
    private ulong OccupiedSeat(ulong pawn)
    {
        // Some seats keep the character pawn; driving can instead possess the
        // vehicle itself. Both are positive occupancy evidence, not ownership.
        ulong seat;
        if (_vehicleActors.TryGetValue(pawn, out ulong type) && Ptr(pawn + O.UObjectClass) == type)
            seat = Ptr(pawn + O.VehicleSeatComponent);
        else
        {
            ulong component = Ptr(pawn + O.VehicleOperatorComponent);
            if (component == 0 || !NamedObject(component, "BHVehicleOperatorComponent")) return 0;
            seat = Ptr(component + 0x198);
        }
        return seat != 0 && NamedObject(seat, "BHVehicleSeatComponent") ? seat : 0;
    }
    private void UpdateOccupants(ulong world, Frame context)
    {
        if (_now >= _nextOccupantRoster)
        {
            _nextOccupantRoster = _now + 500;
            ulong gameState = Ptr(world + O.WorldGameState);
            if (gameState != 0 && Array(gameState + O.GameStatePlayerArray, 2048, out var states))
            {
                _occupantStates = states;
                var present = states.ToHashSet(); present.Add(0);
                foreach (ulong key in _occupants.Keys.Where(k => !present.Contains(k)).ToArray()) _occupants.Remove(key);
            }
            else { _occupantStates = []; _occupants.Clear(); }
            ulong localState = Ptr(context.LocalPawn + O.PawnPlayerState);
            if (localState == 0) localState = Ptr(context.LocalPawn + O.VehiclePlayerState);
            _occupantLocalTeam = localState == 0 ? default : ReadTeam(localState);
            _occupants[0] = new(OccupiedSeat(context.LocalPawn), true, _now);
        }
        // Occupancy is independent of ESP and visibility, with the shared 1000m detail cap.
        int budget = 16, visited = 0;
        while (_occupantStates.Length > 0 && budget > 0 && visited++ < _occupantStates.Length)
        {
            ulong state = _occupantStates[_occupantCursor % _occupantStates.Length];
            _occupantCursor = (_occupantCursor + 1) % _occupantStates.Length;
            if (!Pointer(state)) continue;
            if (_occupants.TryGetValue(state, out var prior) && _now - prior.ReadAt < 500) continue;
            budget--;
            ulong pawn = Ptr(state + O.PlayerStatePawn);
            if(pawn!=context.LocalPawn&&(pawn==0||!Position(pawn,out var position)||!WardogReadRange.Contains(position,context.LocalPosition)))
            {_occupants[state]=new(0,false,_now);continue;}
            ulong seat = pawn == 0 ? 0 : OccupiedSeat(pawn);
            bool friendly = seat != 0 && (pawn == context.LocalPawn || CompareTeams(_occupantLocalTeam, ReadTeam(state)) == Relation.Friendly);
            _occupants[state] = new(seat, friendly, _now);
        }
    }
    private bool ReadActorArray(ulong level, out Arr actors)
    {
        bool Valid(Arr a) => (a.Count == 0 || Pointer(a.Data)) && a.Count >= 0 && a.Count <= 32768 && a.Capacity >= a.Count && a.Capacity <= 131072;
        if (Read(level + 0xA0, out actors) && Valid(actors)) return true;
        ulong container = Ptr(level + O.LevelActorsContainer);
        return container != 0 && Read(container + O.LevelActorsArray, out actors) && Valid(actors);
    }
    private void DiscoverVehicles(ulong world, Func<ulong[], ulong[]> readPointers)
    {
        if (!_scanning && _now >= _nextVehicleScan)
        {
            _baseVehicle = ObjectAt(O.VehicleBaseClassIndex);
            _modularVehicle = ObjectAt(O.ModularVehicleClassIndex);
            if (_baseVehicle == 0 && _modularVehicle == 0) { _nextVehicleScan = _now + 1000; return; }
            Array(world + O.WorldLevels, 512, out var levels);
            _scanLevels = levels.Append(Ptr(world + O.WorldPersistentLevel)).Where(Pointer).Distinct().ToArray();
            _scanLevel = _actorCursor = _scanTotal = 0; _scanActors = default;
            _foundVehicles.Clear(); _scanFailed = false; _scanning = true;
        }
        if (!_scanning) return;
        if (_actorCursor >= _scanActors.Count)
        {
            if (_scanLevel >= _scanLevels.Length || _scanTotal >= 65536)
            {
                if (!_scanFailed) _vehicleActors.Clear();
                foreach (var entry in _foundVehicles) _vehicleActors[entry.Key] = entry.Value;
                _scanning = false; _nextVehicleScan = _now + 3000;
                return;
            }
            if (!ReadActorArray(_scanLevels[_scanLevel++], out _scanActors)) { _scanFailed = true; _scanActors = default; }
            _actorCursor = 0;
        }
        int count = Math.Min(128, Math.Min(_scanActors.Count - _actorCursor, 65536 - _scanTotal));
        if (count <= 0) return;
        var actors = new ulong[count];
        if (!read(_scanActors.Data + (ulong)_actorCursor * 8, MemoryMarshal.AsBytes(actors.AsSpan())))
        { _scanFailed = true; _actorCursor = _scanActors.Count; return; }
        _actorCursor += count; _scanTotal += count;
        var validActors = actors.Where(Pointer).Distinct().ToArray();
        var addresses = validActors.Select(a => a + O.UObjectClass).ToArray();
        ulong[] classes = readPointers != null ? readPointers(addresses) : addresses.Select(address=>Ptr(address)).ToArray();
        for (int i = 0; i < Math.Min(classes.Length, validActors.Length); i++)
            if (VehicleType(classes[i]) && _foundVehicles.Count < 512)
            {
                _foundVehicles[validActors[i]] = classes[i];
                if (_vehicleActors.Count < 512 || _vehicleActors.ContainsKey(validActors[i])) _vehicleActors[validActors[i]] = classes[i]; // Publish discoveries without waiting for a full level sweep.
            }
    }
    public VehicleFrame CaptureVehicles(WardogVehicleSettings settings, Frame context,
        Func<ulong[], ulong[]> readPointers = null)
    {
        // This check is also enforced by the worker, before any DMA operation.
        if (!settings.ShowVehicles || !context.IsFresh || context.WorldAddress == 0) return VehicleFrame.Empty;
        long started = Stopwatch.GetTimestamp();
        _now = clockMilliseconds?.Invoke() ?? MonotonicMilliseconds;
        ulong world = Ptr(moduleBase + O.GWorld);
        if (world == 0 || world != context.WorldAddress) return VehicleFrame.Empty;
        if (world != _vehicleWorld)
        {
            _vehicleWorld = world; ResetVisualCaches();
            _displayNames.Clear(); _displayPoolValidated = false;
            _vehicleActors.Clear(); _foundVehicles.Clear(); _vehicleTypes.Clear(); _vehicleLabels.Clear(); _vehicleIdentities.Clear(); _namedTypes.Clear();
            _vehicleMeshes.Clear(); _vehicleMeshLabels.Clear(); _vehicleMeshRetry.Clear(); _nextVehicleMeshRead.Clear();
            _occupants.Clear(); _occupantStates = []; _occupantCursor = 0;
            _scanning = false; _nextVehicleScan = _nextOccupantRoster = 0;
        }
        scheduleRead?.Invoke(long.MaxValue);
        try { DiscoverVehicles(world, readPointers); }
        finally { scheduleRead?.Invoke(0); }
        if (!settings.ShowTeammateVehicles || settings.CaptureTeams)
        {
            scheduleRead?.Invoke(_now + 500);
            try { UpdateOccupants(world, context); }
            finally { scheduleRead?.Invoke(0); }
        }
        else { _occupants.Clear(); _nextOccupantRoster = 0; }
        var result = new List<Vehicle>();
        int nameBudget = 2, identityBudget = 2, meshBudget = 2;
        foreach (ulong missing in _vehicleIdentities.Keys.Where(a => !_vehicleActors.ContainsKey(a)).ToArray()) _vehicleIdentities.Remove(missing);
        foreach (ulong missing in _vehicleMeshes.Keys.Where(a => !_vehicleActors.ContainsKey(a)).ToArray()) _vehicleMeshes.Remove(missing);
        foreach (ulong missing in _nextVehicleMeshRead.Keys.Where(a => !_vehicleActors.ContainsKey(a)).ToArray()) _nextVehicleMeshRead.Remove(missing);
        foreach (var (actor, type) in _vehicleActors)
        {
            if (Ptr(actor + O.UObjectClass) != type || !Position(actor, out var position)
                || !WardogReadRange.Contains(position,context.LocalPosition)
                || position.Distance(context.LocalPosition) / 100 > settings.MaximumDistance)
            {_vehicleMeshes.Remove(actor);_vehicleIdentities.Remove(actor);_nextVehicleMeshRead.Remove(actor);continue;}
            var relation=Relation.Unknown;
            if (!settings.ShowTeammateVehicles || settings.CaptureTeams)
            {
                ulong seat = Ptr(actor + O.VehicleSeatComponent);
                if (seat != 0 && _occupants.Values.Any(o => o.Seat == seat && o.Friendly && _now - o.ReadAt <= 2000)) relation=Relation.Friendly;
                if(!settings.ShowTeammateVehicles && relation==Relation.Friendly)continue;
            }
            if (!_vehicleLabels.TryGetValue(type, out string label) && nameBudget-- > 0)
            {
                scheduleRead?.Invoke(long.MaxValue);
                try { if (Read(type + O.ClassNameId, out uint id)) label = VehicleClassLabel(DisplayObjectName(id)); }
                finally { scheduleRead?.Invoke(0); }
                if (!string.IsNullOrWhiteSpace(label) && label != "None")
                {
                    if (_vehicleLabels.Count >= 512) _vehicleLabels.Clear();
                    _vehicleLabels[type] = label;
                }
            }
            _vehicleIdentities.TryGetValue(actor, out var identity);
            if (identity?.Type != type) identity = null;
            if ((identity == null || _now >= identity.NextCheck) && identityBudget > 0)
            {
                identityBudget--;
                uint? objectId = null;
                string rawName = "";
                scheduleRead?.Invoke(long.MaxValue);
                try
                {
                    if (Read(actor + O.UObjectNameId, out uint id))
                    {
                        objectId = id;
                        rawName = DisplayObjectName(id); // Actor identity retained separately from the visible class label.
                    }
                }
                finally { scheduleRead?.Invoke(0); }
                identity = new(type, objectId, rawName, _now + (objectId.HasValue && rawName.Length > 0 ? 5000 : 1000));
                _vehicleIdentities[actor] = identity;
            }
            string meshLabel = ReadVehicleMeshLabel(actor, type, ref meshBudget);
            result.Add(new(actor, position, meshLabel.Length > 0 ? meshLabel : string.IsNullOrWhiteSpace(label) ? "Vehicle" : label)
                { ObjectNameId = identity?.Id, RawObjectName = identity?.RawName ?? "",TeamRelation=relation });
        }
        Span<byte> check = stackalloc byte[8];
        if (!(readFresh ?? read)(moduleBase + O.GWorld, check) || MemoryMarshal.Read<ulong>(check) != world) return VehicleFrame.Empty;
        return new(world, result.ToArray(), started);
    }

    private string ReadVehicleMeshLabel(ulong actor, ulong type, ref int budget)
    {
        _vehicleMeshes.TryGetValue(actor, out var prior);
        if (prior != null && prior.Type != type) { _vehicleMeshes.Remove(actor); _nextVehicleMeshRead.Remove(actor); prior = null; }
        string retained = prior != null && _now - prior.ReadAt is >= 0 and <= 2000 ? prior.Label : "";
        if (_nextVehicleMeshRead.TryGetValue(actor, out long next) && _now < next) return retained;
        if (budget <= 0) return retained;
        budget--;
        _nextVehicleMeshRead[actor] = _now + 1000;
        // Known body component first; the root is the supplied stationary-weapon path.
        // At most two actors (two sources each) checked per vehicle cycle.
        ulong body = Ptr(actor + O.VehicleMesh), root = Ptr(actor + O.ActorRootComponent);
        foreach (ulong component in new[] { body, root }.Where(Pointer).Distinct())
        {
            ulong asset = Ptr(component + O.SkinnedAsset);
            if (asset == 0 || !Read(asset + O.UObjectNameId, out uint id)) continue;
            if (!_vehicleMeshLabels.TryGetValue((asset,id), out string label))
            {
                if (_vehicleMeshRetry.TryGetValue((asset,id), out long retry) && _now < retry) continue;
                if (_vehicleMeshRetry.Count >= 512) _vehicleMeshRetry.Clear();
                _vehicleMeshRetry[(asset,id)] = _now + 1000;
                // A root is not necessarily skeletal; validate the asset class.
                ulong assetType = Ptr(asset + O.UObjectClass);
                if (assetType == 0 || !Read(assetType + O.ClassNameId, out uint typeId)
                    || DisplayObjectName(typeId) != "SkeletalMesh") continue;
                string name = DisplayObjectName(id);
                if (name.Length == 0) continue;
                label = VehicleMeshLabel(name);
                if (_vehicleMeshLabels.Count >= 512) _vehicleMeshLabels.Clear();
                _vehicleMeshLabels[(asset,id)] = label;
                _vehicleMeshRetry.Remove((asset,id));
            }
            if (label.Length == 0) continue;
            // Recheck mutable links before publishing; do not reuse another model's label.
            if (!ReadMapFresh(component + O.SkinnedAsset, out ulong checkAsset) || checkAsset != asset
                || !ReadMapFresh(asset + O.UObjectNameId, out uint checkId) || checkId != id
                || !ReadMapFresh(actor + (component == body ? O.VehicleMesh : O.ActorRootComponent), out ulong checkComponent) || checkComponent != component) continue;
            _vehicleMeshes[actor] = new(type, component, asset, id, label, _now);
            return label;
        }
        _vehicleMeshes.Remove(actor);
        return "";
    }

    public static string VehicleMeshLabel(string name) => name switch
    {
        "SK_STN_05" => "Stingray",
        "SK_Deployable_Mortar" => "L81 Mortar",
        "SK_Mistral_Anti_Aircraft" => "Talon 9K-SAM",
        "SK_Phalanx_CIWS" => "Vanguard CIWS",
        "SK_STN_04" => "Loudspeaker",
        // Names confirmed in-game by the user; exact asset names from the supplied dump.
        "SK_WHL_01" => "Quad",
        "SK_WHL_06" => "Dune Buggy",
        "SK_WHL_02_SUV" => "Kodiak",
        "SK_WHL_02_Pickup" => "Kodiak (suv)",
        "SK_WHL_07" => "Ural",
        "SK_WHL_07_Battle" => "Ural Defender",
        "SK_TNK_02" => "SPH-2",
        "SK_TNK_Base" => "FLAKPANZER GEPARD AA",
        "SK_TNK_04" => "T-72",
        "SK_WHL_08_MG" => "WOLF-110",
        "SK_ROT_03" => "MH6 Littlebird",
        "SK_ROT_04" => "Z20 Lakota",
        "SK_ROT_02" => "Havoc",
        // Preserve useful exact asset names for other models without guessing a loadout.
        _ => name.StartsWith("SK_", StringComparison.Ordinal) ? name[3..].Replace('_', ' ').Trim() : ""
    };

    private static string VehicleClassLabel(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "None"
            || name.StartsWith("Default__", StringComparison.Ordinal)) return "";
        // Generic native classes in the supplied dump identify categories only.
        // Do not call all wheeled vehicles transports or all tracked vehicles tanks.
        string category = name switch
        {
            "WDRotaryVehicle" or "BHRotaryVehiclePawn" => "Helicopter",
            "WDWheeledVehiclePawn" or "BHWheeledVehiclePawn" => "Wheeled vehicle",
            "WDTrackedVehicle" or "BHTrackedVehiclePawn" => "Tracked vehicle",
            "WDAirplaneVehicle" or "BHAirplaneVehiclePawn" => "Airplane",
            "WDStationaryVehicle" or "MStationaryVehicle" => "Stationary vehicle",
            "ModularVehicle" or "BHVehicle" => "Vehicle",
            _ => ""
        };
        if (category.Length != 0) return category;
        // More specific Blueprint class names can preserve their model/variant.
        if (!name.StartsWith("BP_", StringComparison.Ordinal) && !name.StartsWith("B_", StringComparison.Ordinal)) return "";
        if (name.EndsWith("_C", StringComparison.Ordinal)) name = name[..^2];
        name = name.StartsWith("BP_", StringComparison.Ordinal) ? name[3..] : name[2..];
        return name.Replace('_', ' ').Trim();
    }
}

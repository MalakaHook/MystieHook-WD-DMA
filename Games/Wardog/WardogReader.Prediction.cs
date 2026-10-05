using System.Diagnostics;
using System.Runtime.InteropServices;
using O = MystieHook.Games.Wardog.WardogOffsets;
namespace MystieHook.Games.Wardog;

public sealed partial class WardogReader
{
    private (ulong World, ulong Controller, ulong Pawn) _predictionContext;
    private ulong _predictionBehavior, _predictionStats, _cachedPredictionStats;
    private float _cachedPredictionSpeed;
    private long _cachedPredictionStamp, _cachedPredictionAt;

    private (float Speed, long Stamp) ReadPredictionSpeed(ulong world, ulong controller, ulong pawn, ulong pawnOffset, bool enabled, long started)
    {
        var context = (world, controller, pawn);
        if (!enabled || context != _predictionContext)
        {
            _predictionContext = context;
            _predictionBehavior = _predictionStats = _cachedPredictionStats = 0;
            _cachedPredictionSpeed = 0; _cachedPredictionStamp = _cachedPredictionAt = 0;
        }
        if (!enabled) return default;
        // Same bounded player-worker page/scatter exchange, using previous-cycle links.
        var ranges = new List<WardogFrameMemory.Range> { new(pawn + O.WeaponBehaviorComponent, 8) };
        if (Pointer(_predictionBehavior)) ranges.Add(new(_predictionBehavior + 0x440, 8));
        if (Pointer(_predictionStats)) ranges.Add(new(_predictionStats + 0xDD8, 4));
        prefetch?.Invoke(ranges);
        bool behaviorOk = Read(pawn + O.WeaponBehaviorComponent, out ulong behavior);
        ulong stats = 0; float speed = 0;
        bool statsOk = Pointer(_predictionBehavior) && Read(_predictionBehavior + 0x440, out stats);
        bool speedOk = Pointer(_predictionStats) && Read(_predictionStats + 0xDD8, out speed);
        Span<byte> buffer = stackalloc byte[8];
        bool pawnOk = (readFresh ?? read)(controller + pawnOffset, buffer) && MemoryMarshal.Read<ulong>(buffer) == pawn;
        bool stalled = Stopwatch.GetElapsedTime(started).TotalMilliseconds > 200;
        bool links = behaviorOk && behavior == _predictionBehavior && Pointer(behavior)
            && statsOk && stats == _predictionStats && Pointer(stats) && pawnOk && !stalled;
        (float Speed, long Stamp) result = default;
        if (links && speedOk && float.IsFinite(speed) && speed is >=2000 and <=200000)
        {
            _cachedPredictionSpeed = speed; _cachedPredictionStats = stats;
            _cachedPredictionAt = _now; _cachedPredictionStamp = started;
            result = (speed, started);
        }
        else if (links && !speedOk && stats == _cachedPredictionStats && _cachedPredictionStamp != 0
            && _now - _cachedPredictionAt is >=0 and <=150)
            result = (_cachedPredictionSpeed, _cachedPredictionStamp);
        // Invalid data is never a transient read failure eligible for reuse.
        if (speedOk && (!float.IsFinite(speed) || speed is <2000 or >200000) || !pawnOk || stalled)
            _cachedPredictionStamp = 0;
        // Unreadable links remain unresolved, never interpreted as pointer changes.
        if (behaviorOk)
        {
            if (behavior != _predictionBehavior) { _predictionStats = 0; _cachedPredictionStamp = 0; }
            else if (statsOk)
            {
                if (stats != _predictionStats) _cachedPredictionStamp = 0;
                _predictionStats = stats;
            }
            _predictionBehavior = behavior;
        }
        return result;
    }
}

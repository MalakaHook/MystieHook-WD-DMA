using System.Diagnostics;

namespace MystieHook.Games.Wardog;

public enum WardogReadStream { Diagnostics, Camera, Players, Vehicles, Loot, Mortar, PredictionSpeed, Dummies }
public enum WardogReadOperation { Other, Context, PlayerLinks, PlayerIdentity, Position, PlayerRoster,
    PlayerName, Team, Health, LifeState, Camera, MeshDiscovery, BoneMapping, NameLookup,
    SkeletonRead, SelectedBonePrefetch, CurrentItem, MapDiscovery, PredictionGuard, RcsGuard, WorldProof }

// Compiler-supplied method names are translated to fixed labels, never logged raw.
public static class WardogReadOperations
{
    public static WardogReadOperation FromCaller(string caller) => caller switch
    {
        "Capture" or "Controller" => WardogReadOperation.Context,
        "PrefetchPlayerLinks" => WardogReadOperation.PlayerLinks,
        "PlayerClass" or "IsPlayerType" => WardogReadOperation.PlayerIdentity,
        "Position" => WardogReadOperation.Position,
        "Array" => WardogReadOperation.PlayerRoster,
        "Text" => WardogReadOperation.PlayerName,
        "LocalTeam" or "ReadTeam" or "FactionName" or "NumericFactionName" => WardogReadOperation.Team,
        "Health" => WardogReadOperation.Health,
        "LifeState" => WardogReadOperation.LifeState,
        "ReadCamera" or "CaptureCamera" => WardogReadOperation.Camera,
        "SelectBodyMesh" or "MeshAsset" or "LeaderMesh" or "PoseCount" => WardogReadOperation.MeshDiscovery,
        "Map" or "DiscoverBoneInfo" => WardogReadOperation.BoneMapping,
        "Name" or "DecodeName" => WardogReadOperation.NameLookup,
        "ReadSkeleton" => WardogReadOperation.MeshDiscovery,
        "ReadSkeletonMesh" or "PoseOffsets" or "Get" => WardogReadOperation.SkeletonRead,
        "PrefetchSelectedBones" => WardogReadOperation.SelectedBonePrefetch,
        "ReadCurrentItem" or "DisplayObjectName" => WardogReadOperation.CurrentItem,
        "UpdateMapIdentity" or "ReadMapFresh" => WardogReadOperation.MapDiscovery,
        "ReadPredictionSpeed" => WardogReadOperation.PredictionGuard,
        _ => WardogReadOperation.Other
    };
}

// Requested work, not physical PCIe/USB throughput. Retains no addresses or contents.
public sealed class WardogReadStatistics
{
    public sealed record Sample(WardogReadStream Stream, long Transfers, long Ranges, long RequestedBytes,
        long VirtualPageVisits, double RequestedKiBPerSecond, double WaitP95, double TransferP95,
        double LongestTransfer, double TotalWait, double TotalTransfer);
    private sealed class Counters
    {
        public long Transfers, Ranges, Bytes, Pages;
        public double TotalWait, TotalTransfer, Longest;
        public readonly double[] Wait = new double[1024], Transfer = new double[1024];
        public int Cursor, Count;
    }
    private readonly object _sync = new();
    private readonly Counters[] _streams = Enum.GetValues<WardogReadStream>().Select(_ => new Counters()).ToArray();
    private readonly long _started = Stopwatch.GetTimestamp();
    private long _lastSnapshot;
    private Sample[] _cached = [];

    public void Record(WardogReadStream stream, int ranges, long bytes, int virtualPages,
        double waitMilliseconds, double transferMilliseconds)
    {
        if ((uint)stream >= _streams.Length || ranges < 0 || bytes < 0 || virtualPages < 0
            || !double.IsFinite(waitMilliseconds) || !double.IsFinite(transferMilliseconds)
            || waitMilliseconds < 0 || transferMilliseconds < 0) return;
        lock (_sync)
        {
            var s = _streams[(int)stream];
            s.Transfers++; s.Ranges += ranges; s.Bytes += bytes; s.Pages += virtualPages;
            s.TotalWait += waitMilliseconds; s.TotalTransfer += transferMilliseconds;
            s.Longest = Math.Max(s.Longest, transferMilliseconds);
            s.Wait[s.Cursor] = waitMilliseconds; s.Transfer[s.Cursor] = transferMilliseconds;
            s.Cursor = (s.Cursor + 1) % s.Wait.Length; s.Count = Math.Min(s.Count + 1, s.Wait.Length);
        }
    }

    public Sample[] Snapshot()
    {
        lock (_sync)
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastSnapshot != 0 && Stopwatch.GetElapsedTime(_lastSnapshot, now).TotalSeconds < 1) return _cached;
            _lastSnapshot = now;
            double seconds = Math.Max(.001, Stopwatch.GetElapsedTime(_started, now).TotalSeconds);
            static double P95(double[] values, int count)
            {
                var copy = values.AsSpan(0, count).ToArray(); Array.Sort(copy);
                return count == 0 ? 0 : copy[(int)Math.Ceiling(count * .95) - 1];
            }
            _cached = _streams.Select((s, index) => new Sample((WardogReadStream)index,
                s.Transfers, s.Ranges, s.Bytes, s.Pages, s.Bytes / 1024.0 / seconds,
                P95(s.Wait, s.Count), P95(s.Transfer, s.Count), s.Longest, s.TotalWait, s.TotalTransfer))
                .Where(s => s.Transfers != 0).ToArray();
            return _cached;
        }
    }
}

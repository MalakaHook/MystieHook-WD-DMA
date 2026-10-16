using System.Diagnostics;

namespace MystieHook.Games.Wardog;

public enum WardogDmaPriority { Camera, Player, Vehicle }

// Prioritize small camera transfers between batches; an in-flight native call cannot be preempted.
public sealed class WardogDmaGate(bool contentionDiagnostics = WardogReadOptimization.Enabled,
    Func<long> timestamp = null)
{
    public enum TransferKind { Unknown, Direct, Scatter }
    public readonly record struct Transfer(WardogReadStream Stream, TransferKind Kind, int Ranges, long Bytes,
        WardogReadOperation Operation = WardogReadOperation.Other);
    public sealed record CameraContention(double WaitMs, double OccupiedMs, int BlockingTransfers,
        Transfer LongestBlocker, double LongestOverlapMs, double BlockerHoldMs)
    { public long WaitStarted { get; init; } }
    private long Now => timestamp?.Invoke() ?? Stopwatch.GetTimestamp();
    private static double Milliseconds(long ticks) => Math.Max(0, ticks * 1000d / Stopwatch.Frequency);
    private readonly Queue<CameraContention> _contentions = new();
    private readonly bool _contentionDiagnostics = contentionDiagnostics;
    internal int PendingCount { get { lock (_sync) return _waiting.Count; } }
    public CameraContention[] DrainCameraContentions()
    {
        lock (_sync) { var result = _contentions.ToArray(); _contentions.Clear(); return result; }
    }
    public WardogReadStatistics Statistics { get; } = WardogReadOptimization.Enabled ? new() : null;
    private sealed class Waiter(WardogDmaPriority priority, long started, Transfer transfer)
    {
        public readonly WardogDmaPriority Priority = priority;
        public readonly long Started = started;
        public readonly Transfer Transfer = transfer;
        public long Acquired, Occupied, LongestOverlap, BlockerHold;
        public int Blocks;
        public Transfer Blocker;
    }
    private readonly object _sync = new();
    private readonly List<Waiter> _waiting = new();
    private bool _busy;
    public IDisposable Enter(WardogDmaPriority priority, CancellationToken token, Transfer transfer = default)
    {
        var waiter = new Waiter(priority, Now, transfer);
        lock (_sync)
        {
            _waiting.Add(waiter);
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    // An aged waiter gets one turn so background work cannot starve forever.
                    var next = _waiting.FirstOrDefault(w => Stopwatch.GetElapsedTime(w.Started).TotalMilliseconds >= 50)
                        ?? _waiting.OrderBy(w => w.Priority).First();
                    if (!_busy && ReferenceEquals(next, waiter))
                    {
                        _busy = true;
                        waiter.Acquired = Now;
                        return new Lease(this, waiter);
                    }
                    Monitor.Wait(_sync, 5);
                }
            }
            finally { _waiting.Remove(waiter); Monitor.PulseAll(_sync); }
        }
    }
    private sealed class Lease(WardogDmaGate owner, Waiter holder) : IDisposable
    {
        private WardogDmaGate _owner = owner;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null) return;
            lock (owner._sync)
            {
                if (owner._contentionDiagnostics)
                {
                    long ended = owner.Now;
                    foreach (var waiter in owner._waiting)
                    {
                        if (waiter.Priority != WardogDmaPriority.Camera) continue;
                        long overlap = Math.Max(0, ended - Math.Max(waiter.Started, holder.Acquired));
                        if (overlap == 0) continue;
                        waiter.Occupied += overlap; waiter.Blocks++;
                        if (overlap > waiter.LongestOverlap)
                        {
                            waiter.LongestOverlap = overlap; waiter.Blocker = holder.Transfer;
                            waiter.BlockerHold = Math.Max(0, ended - holder.Acquired);
                        }
                    }
                    if (holder.Priority == WardogDmaPriority.Camera && Milliseconds(holder.Acquired - holder.Started) >= 50)
                    {
                        if (owner._contentions.Count == 32) owner._contentions.Dequeue();
                        owner._contentions.Enqueue(new(Milliseconds(holder.Acquired - holder.Started),
                            Milliseconds(holder.Occupied), holder.Blocks, holder.Blocker,
                            Milliseconds(holder.LongestOverlap), Milliseconds(holder.BlockerHold)) {WaitStarted=holder.Started});
                    }
                }
                owner._busy = false; Monitor.PulseAll(owner._sync);
            }
        }
    }
}

public static class WardogDmaChunks
{
    public const int MaximumRequests = 256, MaximumBytes = 64 * 1024;
    public static IEnumerable<List<WardogReadBatch.Request>> Partition(IEnumerable<WardogReadBatch.Request> requests,
        int maximumRequests = MaximumRequests, int maximumBytes = MaximumBytes)
    {
        if (maximumRequests is < 1 or > MaximumRequests || maximumBytes is < 1 or > MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumRequests));
        var chunk = new List<WardogReadBatch.Request>(maximumRequests);
        int bytes = 0;
        foreach (var request in requests)
        {
            if (request.Bytes.Length > maximumBytes) throw new ArgumentException("Oversized requests must use the split direct-read path.");
            if (chunk.Count == maximumRequests || bytes + request.Bytes.Length > maximumBytes)
            {
                yield return chunk;
                chunk = new(maximumRequests); bytes = 0;
            }
            chunk.Add(request); bytes += request.Bytes.Length;
        }
        if (chunk.Count != 0) yield return chunk;
    }
}

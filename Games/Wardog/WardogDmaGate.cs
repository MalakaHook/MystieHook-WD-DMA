using System.Diagnostics;

namespace MystieHook.Games.Wardog;

public enum WardogDmaPriority { Camera, Player, Vehicle }

// Prioritize small camera transfers between batches; an in-flight native call cannot be preempted.
public sealed class WardogDmaGate
{
    private sealed record Waiter(WardogDmaPriority Priority, long Started);
    private readonly object _sync = new();
    private readonly List<Waiter> _waiting = new();
    private bool _busy;
    public IDisposable Enter(WardogDmaPriority priority, CancellationToken token)
    {
        var waiter = new Waiter(priority, Stopwatch.GetTimestamp());
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
                        return new Lease(this);
                    }
                    Monitor.Wait(_sync, 5);
                }
            }
            finally { _waiting.Remove(waiter); Monitor.PulseAll(_sync); }
        }
    }
    private sealed class Lease(WardogDmaGate owner) : IDisposable
    {
        private WardogDmaGate _owner = owner;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null) return;
            lock (owner._sync) { owner._busy = false; Monitor.PulseAll(owner._sync); }
        }
    }
}

public static class WardogDmaChunks
{
    public const int MaximumRequests = 256, MaximumBytes = 64 * 1024;
    public static IEnumerable<List<WardogReadBatch.Request>> Partition(IEnumerable<WardogReadBatch.Request> requests)
    {
        var chunk = new List<WardogReadBatch.Request>(MaximumRequests);
        int bytes = 0;
        foreach (var request in requests)
        {
            if (request.Bytes.Length > MaximumBytes) throw new ArgumentException("Oversized requests must use the split direct-read path.");
            if (chunk.Count == MaximumRequests || bytes + request.Bytes.Length > MaximumBytes)
            {
                yield return chunk;
                chunk = new(MaximumRequests); bytes = 0;
            }
            chunk.Add(request); bytes += request.Bytes.Length;
        }
        if (chunk.Count != 0) yield return chunk;
    }
}

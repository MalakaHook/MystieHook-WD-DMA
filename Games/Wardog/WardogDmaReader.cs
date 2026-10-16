using System.Diagnostics;
using VmmSharpEx;
using VmmSharpEx.Options;

namespace MystieHook.Games.Wardog;

internal sealed class WardogDmaReader(Vmm vmm, uint pid, CancellationToken token,
    WardogDmaGate gate = null, WardogDmaPriority priority = WardogDmaPriority.Player,
    WardogReadStream stream = WardogReadStream.Diagnostics) : IDisposable
{
    private VmmSharpEx.Scatter.VmmScatter _scatter;
    private readonly HashSet<ulong> _metricPages = WardogReadOptimization.Enabled ? new() : null;
    private double _lastWaitMilliseconds;
    private int ChunkBytes => WardogReadOptimization.Enabled && Priority == WardogDmaPriority.Vehicle
        ? WardogReadOptimization.BackgroundBytes : WardogDmaChunks.MaximumBytes;
    private int ChunkRequests => WardogReadOptimization.Enabled && Priority == WardogDmaPriority.Vehicle
        ? WardogReadOptimization.BackgroundRequests : WardogDmaChunks.MaximumRequests;
    // Changed only by this reader's owning worker, between synchronous operations.
    public WardogDmaPriority Priority { get; set; } = priority;
    public WardogReadOperation Operation { get; set; }
    public WardogReadOperation LongestTransferOperation { get; private set; }
    public void Dispose() { _scatter?.Dispose(); _scatter = null; }
    public double WaitMilliseconds { get; private set; }
    public double TransferMilliseconds { get; private set; }
    public int Operations { get; private set; }
    public long LastReadStarted { get; private set; }
    public double LongestTransferMilliseconds { get; private set; }
    public string LongestTransferKind { get; private set; } = "none";
    private double RecordTransfer(long started, string kind)
    {
        double duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        TransferMilliseconds += duration;
        if (duration > LongestTransferMilliseconds) { LongestTransferMilliseconds = duration; LongestTransferKind = kind; LongestTransferOperation = Operation; }
        return duration;
    }
    public void ResetMetrics() { WaitMilliseconds = TransferMilliseconds = LongestTransferMilliseconds = 0; Operations = 0; LongestTransferKind = "none"; LongestTransferOperation = WardogReadOperation.Other; Operation = WardogReadOperation.Other; }
    private IDisposable Enter(WardogDmaGate.TransferKind kind, int ranges, long bytes)
    {
        token.ThrowIfCancellationRequested();
        long started = Stopwatch.GetTimestamp();
        var source = stream == WardogReadStream.Players && Priority == WardogDmaPriority.Vehicle
            ? WardogReadStream.Vehicles : stream;
        var lease = gate?.Enter(Priority, token, new(source, kind, ranges, bytes, Operation));
        _lastWaitMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        WaitMilliseconds += _lastWaitMilliseconds;
        return lease;
    }
    private void AddMetricPages(ulong address, int length)
    {
        if (length == 0 || address > ulong.MaxValue - (ulong)(length - 1)) return;
        ulong last = (address + (ulong)length - 1) & ~4095UL;
        for (ulong page = address & ~4095UL;; page += 4096)
        { _metricPages.Add(page); if (page == last) break; }
    }
    private void RecordStatistics(int ranges, long bytes, double duration)
    {
        var kind = stream == WardogReadStream.Players && Priority == WardogDmaPriority.Vehicle
            ? WardogReadStream.Vehicles : stream;
        gate?.Statistics?.Record(kind, ranges, bytes, _metricPages.Count, _lastWaitMilliseconds, duration);
    }
    public unsafe bool Read(ulong address, Span<byte> bytes)
    {
        int maximum = gate == null ? Math.Max(1, bytes.Length) : ChunkBytes;
        for (int offset = 0; offset < bytes.Length; offset += maximum)
        {
            int length = Math.Min(maximum, bytes.Length - offset);
            using var lease = Enter(WardogDmaGate.TransferKind.Direct, 1, length);
            long started = Stopwatch.GetTimestamp();
            if (gate?.Statistics != null) { _metricPages.Clear(); AddMetricPages(address + (ulong)offset, length); }
            if(offset==0)LastReadStarted=started;
            bool success;
            try
            {
                Operations++;
                fixed (byte* buffer = bytes.Slice(offset, length))
                    success = vmm.MemRead(pid, address + (ulong)offset, (nint)buffer, (uint)length,
                        out uint count, VmmFlags.NOCACHE) && count == length;
            }
            finally
            {
                double duration = RecordTransfer(started, "direct");
                if (gate?.Statistics != null) { lease?.Dispose(); RecordStatistics(1, length, duration); }
            }
            if (!success) { bytes.Clear(); return false; }
        }
        token.ThrowIfCancellationRequested();
        return true;
    }
    public ulong[] ReadPointers(ulong[] addresses)
    {
        if (addresses.Length == 0) return [];
        var requests = addresses.Select(a => new WardogReadBatch.Request(a, 8)).ToArray();
        Scatter(requests);
        return requests.Select(r => r.Success ? BitConverter.ToUInt64(r.Bytes) : 0UL).ToArray();
    }
    public void Scatter(IReadOnlyCollection<WardogReadBatch.Request> requests)
    {
        foreach (var request in requests) {request.Success = false;request.SampleStarted=0;}
        if (gate == null) { ScatterChunk(requests); return; }
        int maximum = ChunkBytes;
        foreach (var request in requests.Where(r => r.Bytes.Length > maximum))
        {
            request.Success = Read(request.Address, request.Bytes);
            request.SampleStarted=request.Success?LastReadStarted:0;
        }
        foreach (var chunk in WardogDmaChunks.Partition(requests.Where(r => r.Bytes.Length <= maximum), ChunkRequests, maximum))
            ScatterChunk(chunk);
    }
    private void ScatterChunk(IReadOnlyCollection<WardogReadBatch.Request> requests)
    {
        using var lease = Enter(WardogDmaGate.TransferKind.Scatter, requests.Count, requests.Sum(r => (long)r.Bytes.Length));
        long started = Stopwatch.GetTimestamp();
        long requestedBytes = 0;
        if (gate?.Statistics != null)
        {
            _metricPages.Clear();
            foreach (var request in requests)
            { requestedBytes += request.Bytes.Length; AddMetricPages(request.Address, request.Bytes.Length); }
        }
        try
        {
            Operations++;
            var handle = _scatter ??= vmm.CreateScatter(pid, VmmFlags.NOCACHE);
            foreach (var request in requests)
            {
                request.SampleStarted=started;
                request.Success = handle.PrepareRead(request.Address, (uint)request.Bytes.Length);
            }
            handle.Execute();
            token.ThrowIfCancellationRequested();
            foreach (var request in requests)
                request.Success = request.Success && handle.ReadSpan<byte>(request.Address, request.Bytes.AsSpan());
        }
        finally
        {
            try { _scatter?.Clear(VmmFlags.NOCACHE, pid); }
            finally
            {
                double duration = RecordTransfer(started, "scatter");
                if (gate?.Statistics != null) { lease?.Dispose(); RecordStatistics(requests.Count, requestedBytes, duration); }
            }
        }
    }
}

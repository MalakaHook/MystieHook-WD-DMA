using System.Diagnostics;
using VmmSharpEx;
using VmmSharpEx.Options;

namespace MystieHook.Games.Wardog;

internal sealed class WardogDmaReader(Vmm vmm, uint pid, CancellationToken token,
    WardogDmaGate gate = null, WardogDmaPriority priority = WardogDmaPriority.Player) : IDisposable
{
    private VmmSharpEx.Scatter.VmmScatter _scatter;
    // Changed only by this reader's owning worker, between synchronous operations.
    public WardogDmaPriority Priority { get; set; } = priority;
    public void Dispose() { _scatter?.Dispose(); _scatter = null; }
    public double WaitMilliseconds { get; private set; }
    public double TransferMilliseconds { get; private set; }
    public int Operations { get; private set; }
    public double LongestTransferMilliseconds { get; private set; }
    public string LongestTransferKind { get; private set; } = "none";
    private void RecordTransfer(long started, string kind)
    {
        double duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        TransferMilliseconds += duration;
        if (duration > LongestTransferMilliseconds) { LongestTransferMilliseconds = duration; LongestTransferKind = kind; }
    }
    public void ResetMetrics() { WaitMilliseconds = TransferMilliseconds = LongestTransferMilliseconds = 0; Operations = 0; LongestTransferKind = "none"; }
    private IDisposable Enter()
    {
        token.ThrowIfCancellationRequested();
        long started = Stopwatch.GetTimestamp();
        var lease = gate?.Enter(Priority, token);
        WaitMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return lease;
    }
    public unsafe bool Read(ulong address, Span<byte> bytes)
    {
        int maximum = gate == null ? Math.Max(1, bytes.Length) : WardogDmaChunks.MaximumBytes;
        for (int offset = 0; offset < bytes.Length; offset += maximum)
        {
            int length = Math.Min(maximum, bytes.Length - offset);
            using var lease = Enter();
            long started = Stopwatch.GetTimestamp();
            bool success;
            try
            {
                Operations++;
                fixed (byte* buffer = bytes.Slice(offset, length))
                    success = vmm.MemRead(pid, address + (ulong)offset, (nint)buffer, (uint)length,
                        out uint count, VmmFlags.NOCACHE) && count == length;
            }
            finally { RecordTransfer(started, "direct"); }
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
        foreach (var request in requests) request.Success = false;
        if (gate == null) { ScatterChunk(requests); return; }
        foreach (var request in requests.Where(r => r.Bytes.Length > WardogDmaChunks.MaximumBytes))
            request.Success = Read(request.Address, request.Bytes);
        foreach (var chunk in WardogDmaChunks.Partition(requests.Where(r => r.Bytes.Length <= WardogDmaChunks.MaximumBytes)))
            ScatterChunk(chunk);
    }
    private void ScatterChunk(IReadOnlyCollection<WardogReadBatch.Request> requests)
    {
        using var lease = Enter();
        long started = Stopwatch.GetTimestamp();
        try
        {
            Operations++;
            var handle = _scatter ??= vmm.CreateScatter(pid, VmmFlags.NOCACHE);
            foreach (var request in requests)
                request.Success = handle.PrepareRead(request.Address, (uint)request.Bytes.Length);
            handle.Execute();
            token.ThrowIfCancellationRequested();
            foreach (var request in requests)
                request.Success = request.Success && handle.ReadSpan<byte>(request.Address, request.Bytes.AsSpan());
        }
        finally
        {
            try { _scatter?.Clear(VmmFlags.NOCACHE, pid); }
            finally { RecordTransfer(started, "scatter"); }
        }
    }
}

using VmmSharpEx;

namespace MystieHook.Games.Wardog;

internal sealed class WardogFactionInspector
{
    internal sealed record Request(bool Enabled, ulong Pawn);
    private Request _request = new(false, 0);
    private WardogReader.FactionInspection _current;
    private Task _worker = Task.CompletedTask;
    private CancellationTokenSource _cancel;
    private readonly object _sync = new();
    public Request Selection => Volatile.Read(ref _request);
    public WardogReader.FactionInspection Current => Volatile.Read(ref _current);
    public void Select(bool enabled, ulong pawn)
    {
        lock (_sync)
        {
            if (Selection == new Request(enabled, pawn)) return;
            Volatile.Write(ref _request, new(enabled, pawn));
            Volatile.Write(ref _current, null);
            _cancel?.Cancel();
        }
    }
    public void EnsureStarted(Vmm connection, uint pid, ulong moduleBase, WardogDmaGate gate,
        Func<WardogReader.Frame> context, CancellationToken attachmentToken)
    {
        lock (_sync)
        {
            if (!Selection.Enabled || !_worker.IsCompleted || attachmentToken.IsCancellationRequested) return;
            _cancel?.Dispose();
            _cancel = CancellationTokenSource.CreateLinkedTokenSource(attachmentToken);
            var token = _cancel.Token;
            var selection = Selection;
            _worker = Task.Run(async () =>
            {
                try
                {
                    using var dma = new WardogDmaReader(connection, pid, token, gate, WardogDmaPriority.Vehicle);
                    while (!token.IsCancellationRequested)
                    {
                        var frame = context();
                        ulong pawn = selection.Pawn == 0 ? frame.LocalPawn : selection.Pawn;
                        WardogReader.FactionInspection sample = null;
                        if (frame.IsFresh && (pawn == frame.LocalPawn || frame.Players.Any(p => p.Address == pawn)
                            || frame.RadarPlayers.Any(p => p.Address == pawn)))
                        {
                            int calls = 0;
                            bool Read(ulong address, Span<byte> destination)
                            {
                                token.ThrowIfCancellationRequested();
                                // Hard cap includes name decoding. Each operation releases the shared gate.
                                if (++calls > 128 || destination.Length > 4096) { destination.Clear(); return false; }
                                return dma.Read(address, destination);
                            }
                            // No cache crosses a sample or identity change in this optional diagnostic reader.
                            sample = new WardogReader(moduleBase, Read).InspectFaction(frame, pawn);
                        }
                        lock (_sync)
                        {
                            if (token.IsCancellationRequested || !ReferenceEquals(selection, Selection)) break;
                            Volatile.Write(ref _current, WardogReader.InspectionMatches(sample, context(), pawn) ? sample : null);
                        }
                        await Task.Delay(1000, token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception)
                {
                    Volatile.Write(ref _current, null);
                    try { await Task.Delay(1000, token); } catch (OperationCanceledException) { }
                }
            });
        }
    }
    public void StopAndWait()
    {
        lock (_sync) { _cancel?.Cancel(); Volatile.Write(ref _current, null); }
        _worker.GetAwaiter().GetResult();
    }
}

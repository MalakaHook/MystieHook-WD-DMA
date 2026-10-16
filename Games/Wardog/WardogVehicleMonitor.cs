using System.Diagnostics;
using VmmSharpEx;

namespace MystieHook.Games.Wardog;

internal sealed class WardogVehicleMonitor
{
    private readonly object _lifecycle = new();
    private WardogVehicleSettings _settings = new();
    private WardogReader.VehicleFrame _frame = WardogReader.VehicleFrame.Empty;
    private CancellationTokenSource _cancel;
    private Task _worker = Task.CompletedTask;
    public WardogVehicleSettings Settings
    {
        get => Volatile.Read(ref _settings);
        set
        {
            lock (_lifecycle)
            {
                Volatile.Write(ref _settings, value ?? new());
                Volatile.Write(ref _frame, WardogReader.VehicleFrame.Empty);
                if (!Settings.ShowVehicles) _cancel?.Cancel();
            }
        }
    }

    public WardogReader.VehicleFrame Current => Volatile.Read(ref _frame);
    internal void Publish(WardogReader.VehicleFrame frame) => Volatile.Write(ref _frame, frame);
    // Called only by the connection worker. Every worker owns its own reader,
    // caches, cancellation token and native scatter handles.
    public void EnsureStarted(Vmm connection, uint pid, ulong moduleBase,
        Func<WardogReader.Frame> playerContext, CancellationToken attachmentToken, WardogDmaGate gate = null)
    {
        lock (_lifecycle)
        {
            if (!Settings.ShowVehicles || !_worker.IsCompleted || attachmentToken.IsCancellationRequested) return;
            _cancel?.Dispose();
            _cancel = CancellationTokenSource.CreateLinkedTokenSource(attachmentToken);
            var token = _cancel.Token;
            _worker = Task.Factory.StartNew(() =>
            {
                try
                {
                    using var dma = new WardogDmaReader(connection, pid, token, gate, WardogDmaPriority.Vehicle,WardogReadStream.Vehicles);
                    var batch = new WardogReadBatch(dma.Read, dma.Scatter);
                    long now = 0, lastSlowLog = 0;
                    var reader = new WardogReader(moduleBase, batch.Read, dma.Read, () => now, batch.ScheduleNext);
                    using var pacer = new WardogPacer(token);
                    while (!token.IsCancellationRequested && Settings.ShowVehicles)
                    {
                        long started = Stopwatch.GetTimestamp();
                        now = WardogReader.MonotonicMilliseconds;
                        dma.ResetMetrics();
                        batch.BeginFrame(now);
                        var settings = Settings;
                        var frame = reader.CaptureVehicles(settings, playerContext(), dma.ReadPointers);
                        if (frame.World != 0) frame = frame with { SampleStarted = started };
                        batch.EndFrame();
                        token.ThrowIfCancellationRequested();
                        if (!Settings.ShowVehicles) break;
                        if (settings == Settings) Volatile.Write(ref _frame, frame);
                        if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 100 && now - lastSlowLog >= 2000)
                        {
                            lastSlowLog = now;
                            MystieHook.Services.StartupDiagnostics.Record($"Vehicle stall: wait={dma.WaitMilliseconds:0.0}ms; dma={dma.TransferMilliseconds:0.0}ms; slowest={dma.LongestTransferKind}/{dma.LongestTransferMilliseconds:0.0}ms");
                        }
                        pacer.WaitNext(started, 20);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    MystieHook.Services.StartupDiagnostics.Record("Vehicle reader stopped: " + ex.Message);
                    // Avoid a tight fault/restart loop on the main reader thread.
                    token.WaitHandle.WaitOne(1000);
                }
                finally { Volatile.Write(ref _frame, WardogReader.VehicleFrame.Empty); }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
    public void Stop()
    {
        lock (_lifecycle)
        {
            _cancel?.Cancel();
            Volatile.Write(ref _frame, WardogReader.VehicleFrame.Empty);
        }
    }
    public void StopAndWait()
    {
        Stop();
        try { _worker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
    }
}

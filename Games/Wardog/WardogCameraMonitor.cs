using System.Diagnostics;
using VmmSharpEx;

namespace MystieHook.Games.Wardog;

internal sealed class WardogCameraMonitor : IDisposable
{
    public sealed record Statistics(double ReadMs, double WaitMs, double Hz, int Failures, string Status)
    {
        public double DmaMs { get; init; }
        public double LongestTransferMs { get; init; }
        public string LongestTransferKind { get; init; } = "none";
    }
    private readonly CancellationTokenSource _stop;
    private readonly Task _worker;
    private WardogReader.CameraFrame _frame = WardogReader.CameraFrame.Empty;
    private Statistics _statistics = new(0, 0, 0, 0, "Starting camera");
    public WardogReader.CameraFrame Current => Volatile.Read(ref _frame);
    public Statistics Stats => Volatile.Read(ref _statistics);

    public WardogCameraMonitor(Vmm connection, uint pid, ulong moduleBase, WardogDmaGate gate,
        Func<WardogReader.Frame> context, CancellationToken token)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        _worker = Task.Factory.StartNew(() =>
        {
            var ct = _stop.Token;
            using var dma = new WardogDmaReader(connection, pid, ct, gate, WardogDmaPriority.Camera,WardogReadStream.Camera);
            var reader = new WardogReader(moduleBase, dma.Read);
            long previous = 0, lastSlowLog = 0;
            int failures = 0;
            try
            {
                using var pacer = new WardogPacer(ct);
                while (!ct.IsCancellationRequested)
                {
                    long started = Stopwatch.GetTimestamp();
                    var players = context();
                    dma.ResetMetrics();
                    bool hasContext = WardogReader.CanCaptureCamera(players);
                    if (hasContext)
                    {
                        var next = reader.CaptureCamera(players);
                        if (next.IsFresh)
                        {
                            Volatile.Write(ref _frame, next);
                            failures = 0;
                        }
                        else
                        {
                            failures++;
                            // Keep original sample age, never refresh its timestamp on failure.
                            if (!Current.IsFresh || Current.World != players.WorldAddress || Current.Controller != players.LocalController)
                                Volatile.Write(ref _frame, WardogReader.CameraFrame.Empty);
                        }
                    }
                    else Volatile.Write(ref _frame, WardogReader.CameraFrame.Empty);
                    Volatile.Write(ref _statistics, new(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        dma.WaitMilliseconds, previous == 0 ? 0 : 1 / Stopwatch.GetElapsedTime(previous, started).TotalSeconds,
                        failures, !hasContext ? "Waiting for player context" : failures > 0 ? "Camera sample failed" : "Camera ready")
                    {
                        DmaMs = dma.TransferMilliseconds,
                        LongestTransferMs = dma.LongestTransferMilliseconds,
                        LongestTransferKind = dma.LongestTransferKind
                    });
                    if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 100
                        && (lastSlowLog == 0 || Stopwatch.GetElapsedTime(lastSlowLog).TotalSeconds >= 2))
                    {
                        lastSlowLog = Stopwatch.GetTimestamp();
                        MystieHook.Services.StartupDiagnostics.Record($"Camera stall: read={Stats.ReadMs:0.0}ms; wait={dma.WaitMilliseconds:0.0}ms; dma={dma.TransferMilliseconds:0.0}ms; slowest={dma.LongestTransferKind}/{dma.LongestTransferMilliseconds:0.0}ms; failures={failures}");
                    }
                    previous = started;
                    pacer.WaitNext(started, 120);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Volatile.Write(ref _statistics, new(0, 0, 0, failures + 1, "Camera stopped: " + ex.Message));
                MystieHook.Services.StartupDiagnostics.Record("Camera worker: " + ex.Message);
            }
            finally { Volatile.Write(ref _frame, WardogReader.CameraFrame.Empty); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Dispose()
    {
        _stop.Cancel();
        try { _worker.GetAwaiter().GetResult(); }
        finally { _stop.Dispose(); }
    }
}

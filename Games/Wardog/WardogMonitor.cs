using System.Diagnostics;
using MystieHook.Services;

namespace MystieHook.Games.Wardog;

// Independent camera worker; player/vehicle scan retains its page cache.
public sealed class WardogMonitor
{
    internal static WardogVisualSettings AcquisitionVisuals(WardogVisualSettings visuals,WardogAimSettings aim)
        =>aim.Enabled||aim.ShowPrediction?visuals with {ShowHeadDot=true,ShowBones=visuals.ShowBones||aim.Bone==4,
            ShowTeammates=visuals.ShowTeammates||!aim.TeamCheck,ShowHealth=true,HeadDotColor=null}:visuals;

    internal static WardogReader.Frame FinalizeRadar(WardogReader.Frame frame,WardogVisualSettings visuals,
        bool aimExpanded,WardogRadarSettings demand)
        =>!aimExpanded||demand.LocalMap?frame:frame with {RadarPlayers=frame.RadarPlayers.Where(p=>
            (visuals.ShowTeammates||p.TeamRelation!=WardogReader.Relation.Friendly)
            &&(visuals.ShowDeadPlayers||!p.Dead)).ToArray()};
    internal readonly WardogFactionInspector FactionInspector = new();
    internal readonly WardogHeldItemInspector HeldItemInspector = new();
    private ConnectionFailureInfo _lastFailure;
    public ConnectionFailureInfo LastFailure => Volatile.Read(ref _lastFailure);
    private void PublishFailure(ConnectionFailureKind kind, string detail)
    {
        Volatile.Write(ref _lastFailure, new(kind, Stopwatch.GetTimestamp(), detail));
        Volatile.Write(ref _frame, WardogReader.Frame.Empty(detail));
    }
    private CancellationTokenSource _cancel;
    private Task _worker = Task.CompletedTask;
    private WardogReader.Frame _frame = WardogReader.Frame.Empty("Stopped");
    private WardogVisualSettings _visuals = new();
    private WardogAimSettings _aim=new();
    public WardogAimSettings Aim { get=>Volatile.Read(ref _aim); set=>Volatile.Write(ref _aim,value); }
    private WardogRcsSettings _rcs=new();
    public WardogRcsSettings Rcs {get=>Volatile.Read(ref _rcs);set=>Volatile.Write(ref _rcs,value??new());}
    private int _targetHz = 120;
    private WardogCameraMonitor _camera;
    private WardogReadStatistics _readStatistics;
    internal WardogReadStatistics.Sample[] ReadStatistics=>Volatile.Read(ref _readStatistics)?.Snapshot()??[];
    private readonly WardogVisualMotion _motion = new();
    private readonly WardogRenderGapDiagnostics _renderGaps = new();
    internal WardogReader.Frame ObservedRenderFrame()
    {
        if(!WardogReadOptimization.Enabled)return RenderFrame;
        long now=Stopwatch.GetTimestamp();var camera=Camera;bool attached=IsAttached;
        var result=_motion.SampleForCamera(now,camera,out var probe);
        if(!attached){result=WardogReader.Frame.Empty("Detached");probe=probe with {Issues=probe.Issues|WardogVisualMotion.DisplayIssue.Detached,Drawable=false};}
        _renderGaps.Observe(now,probe,SessionDiagnostics.Classify(Current,camera,attached,now));
        return result;
    }



    internal WardogCameraMonitor.Statistics CameraStats => Volatile.Read(ref _camera)?.Stats;
    public WardogReader.CameraFrame Camera => Volatile.Read(ref _camera)?.Current ?? WardogReader.CameraFrame.Empty;
    public WardogReader.Frame RenderFrame => IsAttached ? _motion.SampleForCamera(Stopwatch.GetTimestamp(),Camera) : WardogReader.Frame.Empty("Detached");
    public string RenderStatus => RenderFrame.IsDisplayFresh ? "Fresh camera with player snapshots" : "Camera/player data unavailable, mismatched or expired";
    private readonly WardogVehicleMonitor _vehicles = new();
    private readonly WardogPredictionSpeedMonitor _predictionSpeed=new();
    public void RefreshPredictionSpeed()=>_predictionSpeed.Refresh();
    private WardogVehicleSettings _vehicleSettings = new();
    private WardogRadarSettings _radar = new();
    public WardogRadarSettings Radar { get => Volatile.Read(ref _radar); set { if (value == Radar) return; Volatile.Write(ref _radar, value); UpdateVehicleNeeds(); } }
    public WardogVehicleSettings VehicleSettings { get => _vehicleSettings; set { _vehicleSettings=value; UpdateVehicleNeeds(); } }
    private volatile bool _mapPlayers, _mapVehicles, _detectMap;
    public void SetMapDemand(bool players,bool vehicles,bool detectMap=false) { _detectMap=detectMap; if (_mapPlayers==players && _mapVehicles==vehicles) return; _mapPlayers=players; _mapVehicles=vehicles; UpdateVehicleNeeds(); }
    private volatile bool _webRadarDemand;
    public void SetWebRadarDemand(bool enabled) { if(_webRadarDemand==enabled)return; _webRadarDemand=enabled;UpdateVehicleNeeds(); }
    private WardogRadarSettings AcquisitionRadar { get { var r=Radar;return r with {
        Enabled=_mapPlayers || _webRadarDemand || (r.Enabled && r.Players), LocalMap=r.Enabled,
        Names=(r.Enabled && r.Players && r.Names) || _webRadarDemand,
        Range=_mapPlayers || _webRadarDemand ? WardogReadRange.MaximumMetres : r.CaptureRange,WebSharing=_webRadarDemand }; } }
    private void UpdateVehicleNeeds()
    {
        var radar=Radar; var vehicles=_vehicleSettings;
        _vehicles.Settings = vehicles with { ShowVehicles=vehicles.ShowVehicles || _mapVehicles || _webRadarDemand || (radar.Enabled && radar.Vehicles),
            ShowTeammateVehicles=_webRadarDemand || (radar.Enabled && radar.Vehicles) || vehicles.ShowTeammateVehicles, CaptureTeams=_webRadarDemand || (radar.Enabled && radar.Vehicles),
            MaximumDistance=_mapVehicles || _webRadarDemand ? WardogReadRange.MaximumMetres : WardogReadRange.Limit(Math.Max(vehicles.ShowVehicles ? vehicles.MaximumDistance : 0, radar.Enabled && radar.Vehicles ? radar.CaptureRange : 0)) };
    }
    public WardogReader.VehicleFrame Vehicles => _vehicles.Current;
    private int _attached;
    private float _aspectRatio = 16f / 9;
    private bool _visualsEnabled = true;
    public float AspectRatio { set => Volatile.Write(ref _aspectRatio, value); }
    public bool VisualsEnabled { set => Volatile.Write(ref _visualsEnabled, value); }
    public bool IsAttached => Volatile.Read(ref _attached) != 0 && IsRunning;
    public bool IsRunning => !_worker.IsCompleted;
    public WardogReader.Frame Current => Volatile.Read(ref _frame);
    public WardogVisualSettings Visuals
    {
        get => Volatile.Read(ref _visuals);
        set => Volatile.Write(ref _visuals, value ?? new WardogVisualSettings());
    }
    public int TargetHz
    {
        get => Volatile.Read(ref _targetHz);
        set => Volatile.Write(ref _targetHz, Math.Clamp(value,30,120));
    }
    public void Start(string processName)
    {
        if (!_worker.IsCompleted || string.IsNullOrWhiteSpace(processName)) return;
        _cancel?.Dispose();
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        var gate = new WardogDmaGate();
        Volatile.Write(ref _readStatistics,gate.Statistics);
        string process = processName.Trim();
        Volatile.Write(ref _frame, WardogReader.Frame.Empty("Connecting..."));
        Volatile.Write(ref _attached, 0);
        _worker = Task.Factory.StartNew(() =>
        {
            using var inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task inputInitialization = Task.CompletedTask;
            bool inputStarted = false;
            long lastLog = 0;
            try
            {
                if (!DmaMemory.TryAttachOnce(process, out string error))
                { PublishFailure(ConnectionFailureKind.AttachFailed, "Attach failed: " + error); return; }
                uint pid = DmaMemory.Pid;
                token.ThrowIfCancellationRequested();
                Volatile.Write(ref _attached, 1);
                ulong moduleBase = DmaMemory.Base;
                Snapshots.Mutate(s => s with { VmmReady = true, Pid = (int)pid, MainBase = moduleBase, Status = "Attached to Wardog" });
                var connection = DmaMemory.Vmm;
                inputStarted = true;
                inputInitialization = MystieHook.Input.InputManager.BeginInitializeWithRetries(
                    new DmaMemory.VmmSharpExAdapter(connection), TimeSpan.FromSeconds(1), inputCancellation.Token,
                    (ready, error) => { if (ready) StartupDiagnostics.Record("InputManager ready."); });
                using var dma = new WardogDmaReader(connection, pid, token, gate,stream:WardogReadStream.Players);
                var batch = new WardogFrameMemory(dma.Read, dma.Scatter);
                long sampleNow = 0;
                var reader = new WardogReader(moduleBase, batch.Read, dma.Read, () => sampleNow, batch.ScheduleNext, batch.Prefetch,()=>_predictionSpeed.Current,dma.Scatter,readOperation:WardogReadOptimization.Enabled?operation=>dma.Operation=operation:null);
                var prediction = new WardogPrediction();
                Volatile.Write(ref _camera, new WardogCameraMonitor(connection, pid, moduleBase, gate, () => Current, token));
                using var pacer = new WardogPacer(token);
                long previousSample = 0;
                long invalidSince = 0;
                long nextVehicleRead = 0;
                while (!token.IsCancellationRequested)
                {
                    if (!DmaMemory.IsAttached || DmaMemory.Pid != pid || DmaMemory.Base != moduleBase || !ReferenceEquals(DmaMemory.Vmm, connection))
                    { PublishFailure(ConnectionFailureKind.AttachmentChanged, "Attachment changed; reconnect to Wardog"); return; }
                    FactionInspector.EnsureStarted(connection, pid, moduleBase, gate, () => Current, token);
                    HeldItemInspector.EnsureStarted(connection, pid, moduleBase, gate, () => Current, token);
                    _predictionSpeed.Enabled=Aim.NeedsPrediction;
                    _predictionSpeed.EnsureStarted(connection,pid,moduleBase,gate,()=>Current,token);
                    long started = Stopwatch.GetTimestamp();
                    sampleNow = WardogReader.MonotonicMilliseconds;
                    dma.ResetMetrics();
                    batch.BeginFrame(sampleNow);
                    var captureSettings=Volatile.Read(ref _visuals);bool esp=Volatile.Read(ref _visualsEnabled);var aim=Aim;
                    bool needsAimPose = aim.Enabled || aim.ShowPrediction;
                    captureSettings=AcquisitionVisuals(captureSettings,aim);
                    var radarDemand=AcquisitionRadar;
                    var next = reader.Capture(captureSettings, Volatile.Read(ref _aspectRatio), esp||needsAimPose, Camera, radarDemand, _detectMap || Radar.DebugNames || (Radar.Enabled && Radar.AutoMap), aim.NeedsPrediction, Rcs.Active);
                    if (next.Valid && _vehicles.Settings.ShowVehicles && sampleNow >= nextVehicleRead)
                    {
                        nextVehicleRead = sampleNow + 50;
                        var vehicleSettings = _vehicles.Settings;
                        dma.Priority = WardogDmaPriority.Vehicle;
                        try
                        {
                            var vehicleFrame = reader.CaptureVehicles(vehicleSettings, next with { SampleStarted = started }, dma.ReadPointers);
                            if (vehicleSettings == _vehicles.Settings) _vehicles.Publish(vehicleFrame);
                        }
                        finally { dma.Priority = WardogDmaPriority.Player; }
                    }
                    else if (!next.Valid || !_vehicles.Settings.ShowVehicles) _vehicles.Publish(WardogReader.VehicleFrame.Empty);
                    next=FinalizeRadar(next,Volatile.Read(ref _visuals),needsAimPose,radarDemand);
                    batch.EndFrame();
                    token.ThrowIfCancellationRequested();
                    if (next.Valid) invalidSince = 0;
                    else
                    {
                        if (invalidSince == 0) invalidSince = Environment.TickCount64;
                        if (Environment.TickCount64 - invalidSince >= 5000)
                        {
                            PublishFailure(ConnectionFailureKind.DataUnavailable, "No valid player context for five seconds; reconnecting...");
                            return;
                        }
                    }
                    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    next = next with
                    {
                        SampleStarted = started,
                        CompletedAt = Stopwatch.GetTimestamp(),
                        ReadMilliseconds = elapsed,
                        DmaWaitMilliseconds = dma.WaitMilliseconds, DmaMilliseconds = dma.TransferMilliseconds, DmaOperations = dma.Operations,
                        LongestTransferMilliseconds = dma.LongestTransferMilliseconds, LongestTransferKind = dma.LongestTransferKind,
                        RefreshHz = previousSample == 0 ? 0 : 1 / Stopwatch.GetElapsedTime(previousSample, started).TotalSeconds,
                        DirectReads = batch.DirectReads + (next.Valid ? 1 : 0),
                        BatchedReads = batch.BatchedReads
                    };
                    previousSample = started;
                    next = prediction.Observe(next, aim.NeedsPrediction);
                    Volatile.Write(ref _frame, next);
                    _motion.Push(next);
                    if (sampleNow - lastLog >= 10000 || (elapsed >= 100 && sampleNow - lastLog >= 2000))
                    {
                        lastLog = sampleNow;
                        StartupDiagnostics.Record("Skeleton sources: " + reader.DescribeSkeletons());
                        var cs = CameraStats;
                        StartupDiagnostics.Record($"Reader: players={next.Players.Length}; valid={next.Valid}; read={elapsed:0.0}ms; dma={dma.TransferMilliseconds:0.0}ms; wait={dma.WaitMilliseconds:0.0}ms; calls={dma.Operations}; slowest={dma.LongestTransferKind}/{dma.LongestTransferMilliseconds:0.0}ms; operation={dma.LongestTransferOperation}; poseFailures={next.PoseFailures}; poseFallbacks={next.PoseFallbacks}; cameraRead={cs?.ReadMs:0.0}ms; cameraWait={cs?.WaitMs:0.0}ms; render={RenderStatus}; status={next.Status}");
                        if (WardogReadOptimization.Active)
                        {
                            StartupDiagnostics.Record($"Read optimization: pageHits={batch.CacheHits}; newPageBuffers={batch.NewPageBuffers}; retainedPageBuffers={batch.RetainedPageBuffers}");
                            foreach (var s in ReadStatistics)
                                StartupDiagnostics.Record($"Read totals {s.Stream}: transfers={s.Transfers}; ranges={s.Ranges}; requestedBytes={s.RequestedBytes}; virtualPageVisits={s.VirtualPageVisits}; p95Wait={s.WaitP95:0.00}ms; p95Transfer={s.TransferP95:0.00}ms; maxTransfer={s.LongestTransfer:0.00}ms");
                            // Drain on the existing diagnostics cadence, never write files
                            // on the camera worker or while holding the DMA lease.
                            var waits = gate.DrainCameraContentions();
                            var gaps = _renderGaps.Drain();
                            foreach(var gap in gaps.OrderByDescending(g=>g.Frames).Take(4))
                                StartupDiagnostics.Record(WardogRenderGapDiagnostics.Describe(gap,Stopwatch.GetTimestamp()));
                            if(gaps.Length>4)StartupDiagnostics.Record($"Render gap summary: retained={gaps.Length}; additional={gaps.Length-4}");
                            if (waits.Length != 0)
                            {
                                var blocked = waits.MaxBy(w => w.WaitMs);
                                StartupDiagnostics.Record(FormattableString.Invariant(
                                    $"Camera DMA contention: retainedEvents={waits.Length}; startedAgo={Stopwatch.GetElapsedTime(blocked.WaitStarted).TotalMilliseconds:0.0}ms; wait={blocked.WaitMs:0.0}ms; gateOccupied={blocked.OccupiedMs:0.0}ms; blockers={blocked.BlockingTransfers}; longest={blocked.LongestBlocker.Stream}/{blocked.LongestBlocker.Kind}; operation={blocked.LongestBlocker.Operation}; ranges={blocked.LongestBlocker.Ranges}; requestedBytes={blocked.LongestBlocker.Bytes}; overlap={blocked.LongestOverlapMs:0.0}ms; blockerGateHold={blocked.BlockerHoldMs:0.0}ms"));
                            }
                        }
                    }
                    if (next.Valid) pacer.WaitNext(started, Volatile.Read(ref _targetHz));
                    else
                    {
                        pacer.Reset();
                        // Failed connections back off; cancellation wakes this wait immediately.
                        token.WaitHandle.WaitOne(500);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { PublishFailure(ConnectionFailureKind.ReaderError, "Read error: " + ex.Message); }
            finally
            {
                _motion.Clear();
                Volatile.Write(ref _attached, 0);
                _vehicles.StopAndWait();
                FactionInspector.StopAndWait();
                HeldItemInspector.StopAndWait();
                _predictionSpeed.StopAndWait();
                Interlocked.Exchange(ref _camera, null)?.Dispose();
                inputCancellation.Cancel();
                try { inputInitialization.GetAwaiter().GetResult(); } catch { }
                if (inputStarted) MystieHook.Input.InputManager.Shutdown();
                Snapshots.Mutate(s => s with { Pid = 0, MainBase = 0, VmmReady = DmaMemory.Vmm is not null });
                if (token.IsCancellationRequested) Volatile.Write(ref _frame, WardogReader.Frame.Empty("Stopped"));
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Stop()
    {
        _cancel?.Cancel();
        _vehicles.Stop();
        Volatile.Write(ref _attached, 0);
        Volatile.Write(ref _frame, WardogReader.Frame.Empty("Stopped"));
    }
}


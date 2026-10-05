using System.Diagnostics;
using ImGuiNET;
using MystieHook.Gui;
using MystieHook.Services;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogGame
{
    public SessionDiagnostics Diagnostics { get; } = new();
    private readonly long _diagnosticsStarted = Stopwatch.GetTimestamp();
    private int _historyWindow = 1;
    private int _connectionAttempts;
    private bool _reconnectRequested;

    private void ObserveDiagnostics(WardogReader.Frame players, WardogReader.CameraFrame camera)
    {
        long now = Stopwatch.GetTimestamp();
        bool attached = _monitor.IsAttached;
        var phase = _reconnectRequested && _monitor.IsRunning ? RecoveryPhase.Stopping : attached ? RecoveryPhase.Attached :
            _monitor.IsRunning ? RecoveryPhase.Connecting : AutoConnect ? RecoveryPhase.RetryWait : RecoveryPhase.Idle;
        var issue = SessionDiagnostics.Classify(players, camera, attached, now);
        var failure = _monitor.LastFailure;
        var cameraStats = _monitor.CameraStats;
        double delta = ImGui.GetIO().DeltaTime;
        Diagnostics.Observe(new((now - _diagnosticsStarted) / (double)Stopwatch.Frequency,
            players.SampleStarted == 0 ? null : players.ReadMilliseconds,
            camera.SampleStarted == 0 ? null : SessionDiagnostics.Age(camera.SampleStarted, now),
            players.SampleStarted == 0 ? null : SessionDiagnostics.Age(players.SampleStarted, now),
            delta > 0 && double.IsFinite(delta) ? 1 / delta : 0,
            players.DmaMilliseconds, players.DmaWaitMilliseconds, players.BatchedReads, players.DirectReads,
            issue, phase, DmaMemory.IsVmmReady, MystieHook.Input.InputManager.IsReady, MystieHook.Input.Device.connected,
            failure?.Kind ?? ConnectionFailureKind.None, failure is null ? null : (failure.Timestamp - _diagnosticsStarted) / (double)Stopwatch.Frequency)
        {
            PlayerTiming = players.SampleStarted == 0 ? null : new(players.ReadMilliseconds, players.DmaMilliseconds,
                players.DmaWaitMilliseconds, players.LongestTransferMilliseconds, ReadTiming.Classify(players.LongestTransferKind)),
            CameraTiming = cameraStats is null ? null : new(cameraStats.ReadMs, cameraStats.DmaMs,
                cameraStats.WaitMs, cameraStats.LongestTransferMs, ReadTiming.Classify(cameraStats.LongestTransferKind))
        });
    }

    public void DrawConnectionRecovery()
    {
        var status = Diagnostics.Latest;
        double now = (Stopwatch.GetTimestamp() - _diagnosticsStarted) / (double)Stopwatch.Frequency;
        ImGui.SeparatorText("Connection recovery");
        string title = status.Phase switch
        {
            RecoveryPhase.Stopping => "Stopping the previous connection before retrying...",
            RecoveryPhase.Connecting => "Connecting DMA and finding the Wardog process...",
            RecoveryPhase.RetryWait => $"Not attached. Next automatic attempt in {Math.Max(0, (_nextAttempt - Environment.TickCount64) / 1000d):0.0}s.",
            RecoveryPhase.Idle => "Automatic connection is inactive.",
            _ => status.Issues == DataIssue.None ? "Connected - matching fresh camera and player data." : "Attached - waiting for matching fresh data."
        };
        ImGui.TextUnformatted(title);
        SafeText.Disabled($"Attempts this session: {_connectionAttempts} | Last drawable update: {Ago(Diagnostics.LastReadySeconds, now)}");
        ImGui.TextUnformatted($"Last successful player sample: {Ago(Diagnostics.LastPlayerSampleSeconds, now)}");
        ImGui.TextUnformatted($"Last successful camera sample: {Ago(Diagnostics.LastCameraSampleSeconds, now)}");
        if (status.Issues != DataIssue.None) SafeText.Wrapped(DescribeIssues(status.Issues));
        // Local-only detail can include the actual attachment error. It is never passed to the exporter.
        SafeText.Wrapped(_monitor.Current.Status);
        if (_monitor.LastFailure is { } failure)
            SafeText.Wrapped($"Last connection failure ({SessionDiagnostics.Age(failure.Timestamp, Stopwatch.GetTimestamp()) / 1000:0.0}s ago): {failure.Detail}");
        if (ImGui.Button("Reconnect now")) Reconnect();
        SafeText.Disabled("Reconnect waits for the previous workers to stop before starting a new attempt.");
    }

    private static string Ago(double? sample, double now) => sample is double t ? $"{Math.Max(0, now - t):0.0}s ago" : "none yet";
    public static string DescribeIssues(DataIssue issues)
    {
        if ((issues & DataIssue.NotAttached) != 0) return "The Wardog attachment is unavailable. See the connection detail below.";
        if ((issues & DataIssue.ContextMismatch) != 0) return "Camera and players belong to different world/controller contexts. Drawings are withheld until they match.";
        if ((issues & (DataIssue.PlayersUnavailable | DataIssue.PlayersStale)) != 0)
            return (issues & DataIssue.PlayersStale) != 0 ? "Player data exceeded the 250 ms freshness limit." : "Player context is unavailable; this can happen during loading or a world change.";
        return (issues & DataIssue.CameraStale) != 0 ? "Camera data exceeded the 100 ms freshness limit." : "Camera data is unavailable. Waiting for a successful camera update.";
    }

    public void DrawPerformanceHistory() => PerformanceDiagnosticsView.Draw(Diagnostics, ref _historyWindow);
}

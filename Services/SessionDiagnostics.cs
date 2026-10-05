using System.Diagnostics;
using MystieHook.Games.Wardog;

namespace MystieHook.Services;

[Flags]
public enum DataIssue { None = 0, NotAttached = 1, PlayersUnavailable = 2, PlayersStale = 4, CameraUnavailable = 8, CameraStale = 16, ContextMismatch = 32 }
public enum RecoveryPhase { Idle, Connecting, Attached, RetryWait, Stopping }
public enum ConnectionFailureKind { None, AttachFailed, AttachmentChanged, DataUnavailable, ReaderError }
public sealed record ConnectionFailureInfo(ConnectionFailureKind Kind, long Timestamp, string Detail);

public enum DmaTransferKind { None, Direct, Scatter }
// Only completed-worker measurements, never raw operation labels or addresses.
public sealed record ReadTiming(double ReadMs, double DmaMs, double WaitMs, double LongestTransferMs, DmaTransferKind LongestTransferKind)
{
    public static DmaTransferKind Classify(string kind) => kind switch
    { "direct" => DmaTransferKind.Direct, "scatter" => DmaTransferKind.Scatter, _ => DmaTransferKind.None };
    public static ReadTiming Peak(ReadTiming a, ReadTiming b)
    {
        if (a is null) return b;
        if (b is null) return a;
        var longest = a.LongestTransferMs > b.LongestTransferMs ? a : b;
        return longest with { ReadMs = Math.Max(a.ReadMs, b.ReadMs), DmaMs = Math.Max(a.DmaMs, b.DmaMs), WaitMs = Math.Max(a.WaitMs, b.WaitMs) };
    }
}

// Owned by the UI thread. Only immutable numeric snapshots leave it for export.
public readonly record struct DiagnosticObservation(double TimeSeconds, double? ReaderMs, double? CameraAgeMs,
    double? PlayerAgeMs, double Fps, double DmaMs, double WaitMs, int Batch, int Direct,
    DataIssue Issues, RecoveryPhase Phase, bool DmaReady, bool InputReady, bool MakcuReady,
    ConnectionFailureKind LastFailure = ConnectionFailureKind.None, double? FailureTimeSeconds = null)
{
    public ReadTiming PlayerTiming { get; init; }
    public ReadTiming CameraTiming { get; init; }
}
public readonly record struct PerformancePoint(double TimeSeconds, double? ReaderMs, double? CameraAgeMs,
    double? PlayerAgeMs, double MinimumFps, double DmaMs, double WaitMs, int Batch, int Direct,
    int Frames, int UnavailableFrames, DataIssue Issues)
{
    public ReadTiming PlayerTiming { get; init; }
    public ReadTiming CameraTiming { get; init; }
}
public readonly record struct DiagnosticEvent(double TimeSeconds, RecoveryPhase Phase, DataIssue Issues,
    bool DmaReady, bool InputReady, bool MakcuReady, ConnectionFailureKind LastFailure, double? FailureTimeSeconds);

public sealed class SessionDiagnostics
{
    public const int Capacity = 600; // 100 ms buckets, at most sixty seconds.
    private readonly PerformancePoint[] _points = new PerformancePoint[Capacity];
    private readonly DiagnosticEvent[] _events = new DiagnosticEvent[128];
    private int _start, _count, _eventStart, _eventCount;
    private long _bucket = -1;
    private bool _observed;
    public int Count => _count;
    public int EventCount => _eventCount;
    public DiagnosticObservation Latest { get; private set; }
    public double? LastReadySeconds { get; private set; }
    public double? LastPlayerSampleSeconds { get; private set; }
    public double? LastCameraSampleSeconds { get; private set; }
    public PerformancePoint Point(int index) => _points[(_start + index) % Capacity];
    public DiagnosticEvent Event(int index) => _events[(_eventStart + index) % _events.Length];

    public static DataIssue Classify(WardogReader.Frame players, WardogReader.CameraFrame camera, bool attached, long now)
    {
        var result = attached ? DataIssue.None : DataIssue.NotAttached;
        if (!players.Valid || players.SampleStarted == 0) result |= DataIssue.PlayersUnavailable;
        else if (Age(players.SampleStarted, now) >= 250) result |= DataIssue.PlayersStale;
        if (camera.World == 0 || camera.Controller == 0 || camera.SampleStarted == 0) result |= DataIssue.CameraUnavailable;
        else if (Age(camera.SampleStarted, now) >= 100) result |= DataIssue.CameraStale;
        if (players.Valid && camera.World != 0 && camera.Controller != 0 &&
            (players.WorldAddress != camera.World || players.LocalController != camera.Controller)) result |= DataIssue.ContextMismatch;
        return result;
    }
    public static double Age(long sample, long now) => Math.Max(0, (now - sample) * 1000d / Stopwatch.Frequency);
    private static double? Peak(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    public void Observe(DiagnosticObservation value)
    {
        if (!double.IsFinite(value.TimeSeconds) || value.TimeSeconds < 0 || (_observed && value.TimeSeconds < Latest.TimeSeconds)) return;
        // Drop expired data by elapsed time, including after minimization or a long UI stall.
        while (_count > 0 && value.TimeSeconds - Point(0).TimeSeconds >= 60) { _start = (_start + 1) % Capacity; _count--; }
        if (!_observed || value.Phase != Latest.Phase || value.Issues != Latest.Issues || value.DmaReady != Latest.DmaReady ||
            value.InputReady != Latest.InputReady || value.MakcuReady != Latest.MakcuReady || value.FailureTimeSeconds != Latest.FailureTimeSeconds)
        {
            if (_eventCount == _events.Length) { _eventStart = (_eventStart + 1) % _events.Length; _eventCount--; }
            _events[(_eventStart + _eventCount++) % _events.Length] = new(value.TimeSeconds, value.Phase, value.Issues,
                value.DmaReady, value.InputReady, value.MakcuReady, value.LastFailure, value.FailureTimeSeconds);
        }
        Latest = value;
        _observed = true;
        if (value.Issues == DataIssue.None) LastReadySeconds = value.TimeSeconds;
        if ((value.Issues & DataIssue.PlayersUnavailable) == 0 && value.PlayerAgeMs is double pa)
            LastPlayerSampleSeconds = value.TimeSeconds - pa / 1000;
        if ((value.Issues & DataIssue.CameraUnavailable) == 0 && value.CameraAgeMs is double ca)
            LastCameraSampleSeconds = value.TimeSeconds - ca / 1000;
        long bucket = (long)(value.TimeSeconds * 10);
        var point = new PerformancePoint(value.TimeSeconds, value.ReaderMs, value.CameraAgeMs, value.PlayerAgeMs,
            value.Fps, value.DmaMs, value.WaitMs, value.Batch, value.Direct, 1, value.Issues == DataIssue.None ? 0 : 1, value.Issues)
        { PlayerTiming = value.PlayerTiming, CameraTiming = value.CameraTiming };
        if (_count > 0 && bucket == _bucket)
        {
            int slot = (_start + _count - 1) % Capacity;
            var old = _points[slot];
            _points[slot] = point with { ReaderMs = Peak(old.ReaderMs, point.ReaderMs), CameraAgeMs = Peak(old.CameraAgeMs, point.CameraAgeMs),
                PlayerAgeMs = Peak(old.PlayerAgeMs, point.PlayerAgeMs), MinimumFps = Math.Min(old.MinimumFps, point.MinimumFps),
                DmaMs = Math.Max(old.DmaMs, point.DmaMs), WaitMs = Math.Max(old.WaitMs, point.WaitMs),
                Batch = Math.Max(old.Batch, point.Batch), Direct = Math.Max(old.Direct, point.Direct),
                PlayerTiming = ReadTiming.Peak(old.PlayerTiming, point.PlayerTiming),
                CameraTiming = ReadTiming.Peak(old.CameraTiming, point.CameraTiming),
                Frames = old.Frames + 1, UnavailableFrames = old.UnavailableFrames + point.UnavailableFrames, Issues = old.Issues | point.Issues };
        }
        else
        {
            if (_count == Capacity) { _start = (_start + 1) % Capacity; _count--; }
            _points[(_start + _count++) % Capacity] = point;
            _bucket = bucket;
        }
    }

    public PerformancePoint[] CopyPoints() { var result = new PerformancePoint[_count]; for (int i = 0; i < result.Length; i++) result[i] = Point(i); return result; }
    public DiagnosticEvent[] CopyEvents() { var result = new DiagnosticEvent[_eventCount]; for (int i = 0; i < result.Length; i++) result[i] = Event(i); return result; }
}

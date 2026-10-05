namespace MystieHook.Games.Wardog;

/// <summary>
/// Learns the ranges traversed by the reader and fetches them together next frame.
/// Only addresses and buffer allocations survive a frame, never successful read status.
/// New pointers take a direct read once and join the next batch.
/// Owned by the single reader worker; no UI thread memory access.
/// </summary>
public sealed class WardogReadBatch(WardogReader.ReadMemory direct, Action<IReadOnlyCollection<WardogReadBatch.Request>> scatter)
{
    public sealed class Request(ulong address, int size)
    {
        public ulong Address { get; } = address;
        public byte[] Bytes { get; } = new byte[size];
        public bool Success { get; set; }
        internal bool Used;
        internal bool Attempted;
        internal long NextDue;
    }

    private readonly Dictionary<(ulong, int), Request> _ranges = new();
    private readonly List<(ulong, int)> _unused = new();
    private readonly List<Request> _due = new();
    private long _now, _nextDue;
    public int DirectReads { get; private set; }
    public int BatchedReads { get; private set; }

    public void ScheduleNext(long nextDue) => _nextDue = nextDue;

    public void BeginFrame(long? nowMilliseconds = null)
    {
        _now = nowMilliseconds ?? WardogReader.MonotonicMilliseconds;
        _nextDue = 0;
        DirectReads = 0;
        _due.Clear();
        foreach (var request in _ranges.Values)
        {
            request.Success = false;
            request.Used = false;
            request.Attempted = request.NextDue <= _now;
            if (request.Attempted) _due.Add(request);
        }
        BatchedReads = _due.Count;
        if (_due.Count > 0) scatter(_due);
    }

    public bool Read(ulong address, Span<byte> destination)
    {
        if (!WardogReader.Pointer(address) || destination.Length == 0 || destination.Length > 16384) return false;
        var key = (address, destination.Length);
        if (!_ranges.TryGetValue(key, out var request))
        {
            // The reader bounds actor counts and array sizes. Keep a second limit here.
            if (_ranges.Count >= 65536) return false;
            request = new Request(address, destination.Length);
            _ranges.Add(key, request);
        }
        if (!request.Attempted)
        {
            DirectReads++;
            request.Success = direct(address, request.Bytes);
            request.Attempted = true;
        }
        request.NextDue = request.Used ? Math.Min(request.NextDue, _nextDue) : _nextDue;
        request.Used = true;
        if (!request.Success) { destination.Clear(); return false; }
        request.Bytes.AsSpan().CopyTo(destination);
        return true;
    }

    public void EndFrame()
    {
        _unused.Clear();
        foreach (var entry in _ranges)
            if (entry.Value.NextDue == long.MaxValue || (!entry.Value.Used && entry.Value.NextDue <= _now)) _unused.Add(entry.Key);
        foreach (var key in _unused) _ranges.Remove(key);
    }
}

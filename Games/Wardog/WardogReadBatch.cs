namespace MystieHook.Games.Wardog;

/// <summary>
/// Learns the ranges traversed by the reader and fetches them together next frame.
/// Only addresses and buffer allocations survive a frame, never successful read status.
/// New pointers take a direct read once and join the next batch.
/// Owned by the single reader worker; no UI thread memory access.
/// </summary>
public sealed class WardogReadBatch(WardogReader.ReadMemory direct, Action<IReadOnlyCollection<WardogReadBatch.Request>> scatter,
    Func<long> directSampleStarted=null)
{
    public sealed class Request(ulong address, int size)
    {
        public ulong Address { get; } = address;
        public byte[] Bytes { get; } = new byte[size];
        public bool Success { get; set; }
        public long SampleStarted { get; set; }
        internal bool Used;
        internal bool Attempted;
        internal bool VerificationSkipped;
        internal long NextDue;
        internal long LastUsed;
        internal bool GlobalOwner;
        internal HashSet<ulong> Contacts;
    }

    private readonly Dictionary<(ulong, int), Request> _ranges = new();
    private readonly List<(ulong, int)> _unused = new();
    private readonly List<Request> _due = new();
    private long _now, _nextDue;
    private ulong _contact;
    public int DirectReads { get; private set; }
    public int BatchedReads { get; private set; }
    public long ScheduledRangeRetentionMilliseconds {get;init;}

    public void ScheduleNext(long nextDue) => _nextDue = nextDue;
    // Retire only a completed acquisition's exact range, after its proof. This
    // prevents a cached numerical field from joining next cycle's prefetch.
    public void ForgetRange(ulong address,int size)
    {if(_ranges.TryGetValue((address,size),out var request))request.NextDue=long.MaxValue;}

    // Contact ownership retires only that contact's learned ranges. Other
    // alternating contacts keep their scheduled addresses and original stamps.
    public void SetContact(ulong actor)=>_contact=actor;
    public void ForgetContact(ulong actor)
    {
        if(actor==0)return;
        foreach(var request in _ranges.Values)
            if(request.Contacts?.Remove(actor)==true&&request.Contacts.Count==0&&!request.GlobalOwner)
                request.NextDue=long.MaxValue; // Remove at EndFrame, after final proof.
    }

    public void ScheduleEarlier(ulong address,int size,long nextDue)
    {
        if(_ranges.TryGetValue((address,size),out var request))request.NextDue=Math.Min(request.NextDue,nextDue);
    }

    public void BeginFrame(long? nowMilliseconds = null)
    {
        _now = nowMilliseconds ?? WardogReader.MonotonicMilliseconds;
        _nextDue = 0;
        _contact=0;
        DirectReads = 0;
        _due.Clear();
        foreach (var request in _ranges.Values)
        {
            request.Success = false;
            request.SampleStarted = 0;
            request.Used = false;
            request.VerificationSkipped = false;
            request.Attempted = request.NextDue <= _now;
            if (request.Attempted) _due.Add(request);
        }
        BatchedReads = _due.Count;
        if (_due.Count > 0)
        {
            long started=System.Diagnostics.Stopwatch.GetTimestamp();scatter(_due);
            foreach(var request in _due)
                if(request.Success&&request.SampleStarted==0)request.SampleStarted=started;
        }
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
        if(_contact==0)request.GlobalOwner=true;
        else(request.Contacts??=new()).Add(_contact);
        if (!request.Attempted)
        {
            DirectReads++;
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            request.Success = direct(address, request.Bytes);
            request.SampleStarted=request.Success?(directSampleStarted?.Invoke()??started):0;
            request.Attempted = true;
        }
        request.NextDue = request.Used ? Math.Min(request.NextDue, _nextDue) : _nextDue;
        request.Used = true;
        request.LastUsed = _now;
        if (!request.Success) { destination.Clear(); return false; }
        request.Bytes.AsSpan().CopyTo(destination);
        return true;
    }

    public long SampleStarted(ulong address,int size)=>_ranges.TryGetValue((address,size),out var request)
        &&request.Attempted&&request.Success?request.SampleStarted:0;

    // Motion is a first-wave observation, not an identity proof. Re-reading a
    // moving transform for equality would reject every actively moving shell.
    public void SkipVerification(ulong address,int size)
    {
        if(_ranges.TryGetValue((address,size),out var request))request.VerificationSkipped=true;
    }

    public bool VerifyUsed()
    {
        var observed=_ranges.Values.Where(r=>r.Used&&r.Success&&!r.VerificationSkipped).ToArray();
        if(observed.Length>256||observed.Sum(r=>r.Bytes.Length)>65536||observed.Any(r=>r.Bytes.Length>4096))return false;
        if(observed.Length==0)return true;
        var proofs=observed.Select(r=>new Request(r.Address,r.Bytes.Length)).ToArray();
        scatter(proofs);
        for(int index=0;index<proofs.Length;index++)
            if(!proofs[index].Success||!proofs[index].Bytes.AsSpan().SequenceEqual(observed[index].Bytes))return false;
        return true;
    }

    public void EndFrame()
    {
        _unused.Clear();
        foreach (var entry in _ranges)
        {
            var request=entry.Value;
            if(request.NextDue==long.MaxValue){_unused.Add(entry.Key);continue;}
            if(request.Used||request.NextDue>_now)continue;
            // A delayed cycle may prefetch both alternating contact groups.
            // Keep the unvisited group's addresses for its next visit, while
            // every BeginFrame still clears its byte success and timestamp.
            if(request.NextDue>0&&_now-request.LastUsed<ScheduledRangeRetentionMilliseconds)
                request.NextDue=_now+50;
            else _unused.Add(entry.Key);
        }
        foreach (var key in _unused) _ranges.Remove(key);
    }
}

namespace MystieHook.Games.Wardog;

// Reference FrameMemory semantics: one scan's pages only, deduplicated at 4 KB.
// Unlike the reference, retain our 64 KB transfer bound and cancellation in the adapter.
public sealed class WardogFrameMemory(WardogReader.ReadMemory direct,
    Action<IReadOnlyCollection<WardogReadBatch.Request>> scatter)
{
    public readonly record struct Range(ulong Address, int Size);
    private readonly Dictionary<ulong, WardogReadBatch.Request> _pages = new();
    public int DirectReads { get; private set; }
    public int BatchedReads { get; private set; }
    public void BeginFrame(long unused = 0) { _pages.Clear(); DirectReads = BatchedReads = 0; }
    public void EndFrame() { }
    public void ScheduleNext(long unused) { } // Metadata scheduling still belongs to the reader.
    public void Prefetch(IEnumerable<Range> ranges)
    {
        var requests = new List<WardogReadBatch.Request>();
        foreach (var range in ranges)
        {
            if (!WardogReader.Pointer(range.Address) || range.Size is <= 0 or > 1048576 ||
                !WardogReader.Pointer(range.Address + (ulong)range.Size - 1)) continue;
            ulong last = (range.Address + (ulong)range.Size - 1) & ~4095UL;
            for (ulong page = range.Address & ~4095UL; page <= last; page += 4096)
            {
                if (_pages.ContainsKey(page) || _pages.Count >= 16384) continue;
                var request = new WardogReadBatch.Request(page, 4096);
                _pages.Add(page, request); requests.Add(request);
            }
        }
        if (requests.Count == 0) return;
        BatchedReads += requests.Count;
        scatter(requests);
    }
    public bool Read(ulong address, Span<byte> destination)
    {
        if (!WardogReader.Pointer(address) || destination.Length is <= 0 or > 1048576 ||
            !WardogReader.Pointer(address + (ulong)destination.Length - 1)) return false;
        Prefetch([new(address, destination.Length)]);
        for (int offset = 0; offset < destination.Length;)
        {
            ulong current = address + (ulong)offset, page = current & ~4095UL;
            int start = (int)(current - page), length = Math.Min(destination.Length-offset, 4096-start);
            if (!_pages.TryGetValue(page, out var request) || !request.Success)
            {
                DirectReads++;
                bool success = direct(address, destination);
                if (!success) destination.Clear();
                return success;
            }
            request.Bytes.AsSpan(start, length).CopyTo(destination[offset..]);
            offset += length;
        }
        return true;
    }
}


namespace MystieHook.Games.Wardog;

// UI-owned barrier only: acquisition keeps its existing independent schedules.
public sealed class WardogArtilleryMarkerRefresh
{
    private int _map = -1;
    public long RequestedAt { get; private set; }
    public void Select(int map, long now)
    {
        if (map == _map) return;
        _map = map; RequestedAt = now;
    }
    public void Refresh(long now) => RequestedAt = now;
    public bool Accept(WardogReader.Frame frame) => frame.IsFresh && frame.SampleStarted >= RequestedAt;
    public bool Accept(WardogReader.VehicleFrame frame, ulong world) => frame.IsFresh && frame.World == world && frame.SampleStarted >= RequestedAt;
}

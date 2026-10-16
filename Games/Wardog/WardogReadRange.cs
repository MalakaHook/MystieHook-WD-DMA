namespace MystieHook.Games.Wardog;

// User-selected game data horizon. Discovery and position probes must still
// run outside it so contacts can re-enter; feature detail cannot bypass it.
public static class WardogReadRange
{
    public const float MaximumMetres=1000;
    public const double MaximumCentimetres=100000;
    public static float Limit(float requested)=>Math.Min(MaximumMetres,requested);
    public static bool Contains(WardogReader.Vec position,WardogReader.Vec local)
        =>position.Valid()&&local.Valid()&&position.Distance(local)<=MaximumCentimetres;
}

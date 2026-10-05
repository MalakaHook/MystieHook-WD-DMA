using System.Numerics;

namespace MystieHook.Games.Wardog;

// Reference markers from the same wardogs.tech viewer as the imported tiles.
// Positions are normalized image coordinates (top-left origin), not legacy world/map units.
public static class WardogMapLocations
{
    public readonly record struct Marker(string Label, Vector2 ImagePosition);
    private static readonly Marker[] Bakurani =
    [
        new("Tower 1", new(.4916f, .5735f)),
        new("Tower 2", new(.4714f, .5729f)),
        new("Tower 3", new(.4713f, .5516f)),
        new("Tower 4", new(.5104f, .5556f)),
        new("Tower 5", new(.5020f, .5826f)),
        new("Manticore base - Chuta", new(.2455f, .5292f)),
        new("Valkyra base - Khevuli", new(.7208f, .5710f)),
        new("Lonestar base - Baghi", new(.5293f, .7992f))
    ];
    private static readonly Marker[] Ozeti =
    [
        new("Tower 1", new(.5849f, .6163f)),
        new("Tower 2", new(.6126f, .6384f)),
        new("Tower 3", new(.6379f, .6113f)),
        new("Tower 4", new(.6145f, .5872f)),
        new("Manticore base - Malaga", new(.4216f, .4633f)),
        new("Valkyra base - Barcelona", new(.8391f, .5915f)),
        new("Lonestar base - Granada", new(.5113f, .8085f))
    ];
    private static readonly Marker[] Zestafona =
    [
        new("Tower 1", new(.4187f, .3643f)),
        new("Tower 2", new(.4449f, .3587f)),
        new("Tower 3", new(.4283f, .3886f)),
        new("Valkyra base - Austin", new(.2407f, .2374f)),
        new("Manticore base - Chicago", new(.6388f, .2976f)),
        new("Lonestar base - Boston", new(.4151f, .5935f))
    ];

    // The source's default base references: Bakurani, Madrid and Detroit rotations respectively.
    // Do not present these as live spawn detection or retain unverified legacy vendor/zone markers.
    public static ReadOnlySpan<Marker> For(string map) => map switch
    { "bakurani" => Bakurani, "ozeti" => Ozeti, "zestafona" => Zestafona, _ => ReadOnlySpan<Marker>.Empty };
}

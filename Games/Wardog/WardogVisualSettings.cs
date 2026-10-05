namespace MystieHook.Games.Wardog;

public enum WardogBoxStyle { Cornered, Normal }
public enum WardogHealthPosition { Left, Right, Top, Bottom }

public sealed record WardogVisualSettings(bool ShowTeammates = true, bool ShowBones = false,
    bool ShowHeadDot = true, bool ShowName = true, bool ShowHealth = true, float MaximumDistance = 1000,
    int SkeletonHz = 0)
{
    public float NameTextSize { get; init; } = 14;
    public float DistanceTextSize { get; init; } = 14;
    public float FactionTextSize { get; init; } = 14;
    public bool ShowFaction { get; init; }
    public bool ShowCurrentItem { get; init; }
    public bool ShowDeadPlayers { get; init; } = true;
    public bool ShowDownedPlayers { get; init; }
    public bool ShowDistances { get; init; }
    public bool ShowBoxes { get; init; }
    public WardogBoxStyle BoxStyle { get; init; }
    public WardogHealthPosition HealthPosition { get; init; }
    public float HeadDotSize { get; init; } = 3.5f;
    public float BoneThickness { get; init; } = 1.2f;
    public uint? HeadDotColor { get; init; }
    public uint? BoneColor { get; init; }
    public uint? BoxColor { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasVisuals => ShowDownedPlayers || ShowBones || ShowHeadDot || ShowName || ShowFaction || ShowHealth || ShowBoxes || ShowCurrentItem || ShowDistances;

    // Shared by acquisition, overlay and roster so no player visual escapes the filters.
    public bool Allows(WardogReader.Vec position, WardogReader.Vec local, WardogReader.Relation relation,
        bool dead = false, bool downed = false)
        => position.Valid() && local.Valid() && float.IsFinite(MaximumDistance) && MaximumDistance >= 0
        && (!dead || ShowDeadPlayers) && (dead || !downed || ShowDownedPlayers)
        && position.Distance(local) / 100 <= MaximumDistance
        && (ShowTeammates || relation != WardogReader.Relation.Friendly);
}

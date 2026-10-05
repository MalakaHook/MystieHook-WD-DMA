namespace MystieHook.Games.Wardog;

public sealed record WardogVehicleSettings(bool ShowVehicles = false, bool ShowTeammateVehicles = true,
    float MaximumDistance = 1000)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool CaptureTeams { get; init; }
}

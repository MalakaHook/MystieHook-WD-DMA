namespace MystieHook.Games.Wardog;

// Confirmed display labels only. Prediction reads live stats, never this table.
public static class WardogWeaponTable
{
    private static readonly Dictionary<string,string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Id.Item.WEPN_033"]="BUSHMASTER M17S", ["Id.Item.WEPN_029"]="GALIL",
        ["Id.Item.SMG_03"]="PP-19", ["Id.Item.WEPN_028"]="MP5",
        ["Id.Item.LMG_02"]="PKM", ["Id.Item.WEPN_035"]="SCOUT RIFLE",
        ["Id.Item.SR_04"]="AMR 50", ["Id.Item.Judge"]="Judge",
        ["Id.Item.WEPN_026"]="M1911", ["Id.Item.WEPN_032"]="G18X",
        ["Id.Item.WEPN_027"]="DEAGLE", ["Id.Item.Launcher_04"]="VERBA ANTI-AIR",
        ["Id.Item.CGM4"]="MAAWS"
    };
    public static string HeldItemLabel(string identity)
    {
        if (string.IsNullOrEmpty(identity) || !identity.StartsWith("Id.Item.",StringComparison.OrdinalIgnoreCase)) return "";
        return Labels.TryGetValue(identity,out var label) ? label : identity[8..].Replace('.',' ').Replace('_',' ').Trim();
    }
}

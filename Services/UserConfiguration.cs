using System.Text.Json;
using MystieHook.Games;
using MystieHook.Games.Wardog;

namespace MystieHook.Services;

public sealed class WardogPreferences
{
    public string ProcessName { get; set; } = "WardogsClient-Win64-Shipping.exe";
    public bool Overlay { get; set; } = true;
    public int ReadHz { get; set; } = 120;
    public WardogVisualSettings Visuals { get; set; } = new();
    public WardogAimSettings Aim { get; set; } = new();
    public WardogRcsSettings Rcs {get;set;}=new();
    public WardogArtillerySettings Artillery { get; set; } = new();
    public WardogRadarSettings Radar { get; set; } = new();
    public WardogVehicleSettings Vehicles { get; set; } = new();
}

public sealed class UserConfiguration
{
    public int Version { get; set; } = 1;
    public string Game { get; set; } = "Wardog";
    public WardogPreferences Wardog { get; set; } = new();
    public int Monitor { get; set; }
    public bool VSync { get; set; }
    public int FpsCap { get; set; } = 120;
    public bool Borderless { get; set; }
    public bool Fullscreen { get; set; }
    public bool SidebarExpanded { get; set; } = true;
    public bool VisualPreviewOpen { get; set; }
    public string[] FavoriteOptions { get; set; } = [];
    public bool ShowFps { get; set; }

    public string InputPort { get; set; } = "";
    public string Theme { get; set; } = "Wardog Red";
    public KeybindProfile Keybinds { get; set; } = new()
    {
        Binds = new() { new() { Name = "Show menu", Vk = 0x79, Action = "ShowMenu" } }
    };

    public void Validate()
    {
        if (FavoriteOptions is null || FavoriteOptions.Length > 128 || FavoriteOptions.Any(x => x is null || x.Length > 160))
            throw new InvalidDataException("Invalid favorite settings.");
        if (Wardog?.Aim is null || !Wardog.Aim.Valid) throw new InvalidDataException("Invalid aim settings.");
        if(Wardog.Rcs?.Weapons==null||Wardog.Rcs.Weapons.Count>128||Wardog.Rcs.Weapons.Any(p=>p.Key.Length>128||p.Value==null||!float.IsFinite(p.Value.Rate)||Math.Abs(p.Value.Rate)>1000))
            throw new InvalidDataException("Invalid RCS profiles.");
        if (Wardog?.Artillery is null || !Wardog.Artillery.Valid) throw new InvalidDataException("Invalid artillery settings.");
        if (Wardog?.Radar is null || !Wardog.Radar.Valid) throw new InvalidDataException("Invalid radar settings.");
        if (Version != 1) throw new InvalidDataException("Unsupported configuration version.");
        if (Wardog?.Visuals is null || Keybinds?.Binds is null)
            throw new InvalidDataException("Configuration is missing required settings.");
        if (Wardog.Vehicles == null || !float.IsFinite(Wardog.Vehicles.MaximumDistance)
            || Wardog.Vehicles.MaximumDistance < 0 || Wardog.Vehicles.MaximumDistance > 1000)
            throw new InvalidDataException("Vehicle distance must be between 0 and 1000 metres.");
        if (!Enum.IsDefined(Wardog.Visuals.BoxStyle) || !Enum.IsDefined(Wardog.Visuals.HealthPosition))
            throw new InvalidDataException("Invalid box style or health bar position.");
        if (Wardog.ReadHz < 30 || Wardog.ReadHz > 240 || FpsCap < 0 || FpsCap > 1000 || Monitor < 0)
            throw new InvalidDataException("Invalid refresh rate or monitor setting.");
        if (!float.IsFinite(Wardog.Visuals.NameTextSize) || Wardog.Visuals.NameTextSize is < 10 or > 32
            || !float.IsFinite(Wardog.Visuals.DistanceTextSize) || Wardog.Visuals.DistanceTextSize is < 10 or > 32
            || !float.IsFinite(Wardog.Visuals.FactionTextSize) || Wardog.Visuals.FactionTextSize is < 10 or > 32)
            throw new InvalidDataException("Player text sizes must be between 10 and 32 pixels.");
        if (!float.IsFinite(Wardog.Visuals.HeadDotSize) || Wardog.Visuals.HeadDotSize < 1 || Wardog.Visuals.HeadDotSize > 10
            || !float.IsFinite(Wardog.Visuals.BoneThickness) || Wardog.Visuals.BoneThickness < 1 || Wardog.Visuals.BoneThickness > 10)
            throw new InvalidDataException("Dot size and bone thickness must be between 1 and 10.");
        if (!float.IsFinite(Wardog.Visuals.MaximumDistance) || Wardog.Visuals.MaximumDistance < 5 || Wardog.Visuals.MaximumDistance > 5000
            || (Wardog.Visuals.SkeletonHz != 0 && (Wardog.Visuals.SkeletonHz < 10 || Wardog.Visuals.SkeletonHz > 240)))
            throw new InvalidDataException("Invalid visual settings.");
        if (string.IsNullOrWhiteSpace(Wardog.ProcessName) || string.IsNullOrWhiteSpace(Game)
            || string.IsNullOrWhiteSpace(Theme) || InputPort is null)
            throw new InvalidDataException("Invalid configuration text field.");
        if (Theme == "Mambo" + "Signature") Theme = "MamboSignature";
        NormalizeKeybinds(Keybinds);
        if (Keybinds.Binds.Any(b => b.Vk < 1 || b.Vk > 254 || !Enum.IsDefined(b.Mode) || string.IsNullOrWhiteSpace(b.Action)))
            throw new InvalidDataException("Invalid keybind.");
    }

    public static void NormalizeKeybinds(KeybindProfile profile)
    {
        profile.Binds ??= new();
        profile.Binds.RemoveAll(b => b is null || b.Action is "ToggleServiceControl" or "ToggleHomeWindow" or "ToggleKeybindsWindow" or "InitVmm" or "DisposeVmm" or "RefreshProcesses" or "AttachSelectedProcess" or "ToggleTopBar" or "ReconnectGame" or "CenterWindow" or "RestoreDecorations");
        foreach (var bind in profile.Binds)
        {
            if (bind.Action == "ToggleAllMenus") bind.Action = "ShowMenu";
            if (bind.Action == "ShowMenu") bind.Name = "Show menu";
        }
    }
}

public sealed class ConfigurationStore
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MystieHook", "config");
    public string DirectoryPath { get; }
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public ConfigurationStore(string directory = null) => DirectoryPath = directory ?? DefaultDirectory;

    private string ConfigPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name != name.Trim()
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.')
            || name.Contains('/') || name.Contains('\\') || name is "." or ".."
            || System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("Use a short config name without special filename characters.");
        return Path.Combine(DirectoryPath, name + ".config.json");
    }

    public string[] List() => Directory.Exists(DirectoryPath)
        ? Directory.GetFiles(DirectoryPath, "*.config.json").Select(p => Path.GetFileName(p)[..^12]).OrderBy(n => n).ToArray()
        : Array.Empty<string>();

    public UserConfiguration Load(string name)
    {
        var value = JsonSerializer.Deserialize<UserConfiguration>(File.ReadAllText(ConfigPath(name)), Options)
            ?? throw new InvalidDataException("The configuration is empty.");
        value.Validate();
        return value;
    }

    public void Save(string name, UserConfiguration value, bool create = false)
    {
        var path = ConfigPath(name);
        value.Validate();
        Directory.CreateDirectory(DirectoryPath);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, path, !create);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Remember(string name)
    {
        ConfigPath(name);
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, "last-config.txt");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, name);
        File.Move(temporary, path, true);
    }

    public string LastUsed()
    {
        var path = Path.Combine(DirectoryPath, "last-config.txt");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }
}

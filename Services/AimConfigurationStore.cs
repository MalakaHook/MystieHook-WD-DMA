using System.Text.Json;
using MystieHook.Games.Wardog;

namespace MystieHook.Services;

// Called on a background task by the Aim UI; never performs DMA work.
public sealed class AimConfigurationStore
{
    private sealed record Document(int Version, WardogAimSettings Settings);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string DirectoryPath { get; }
    public AimConfigurationStore(string directory = null) => DirectoryPath = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MystieHook", "Aim config");

    private string ConfigPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 48 || name != name.Trim()
            || name.Any(c => !char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_')
            || new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Use 1–48 letters, numbers, spaces, hyphens or underscores for the aim configuration name.");
        return Path.Combine(DirectoryPath, name + ".json");
    }

    public string[] List()
    {
        Directory.CreateDirectory(DirectoryPath);
        return Directory.GetFiles(DirectoryPath, "*.json").Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void Save(string name, WardogAimSettings settings)
    {
        string path = ConfigPath(name);
        if (settings is null || !settings.Valid) throw new InvalidDataException("Invalid aim settings or calibration values.");
        Directory.CreateDirectory(DirectoryPath);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(1, settings), Options));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public WardogAimSettings Load(string name)
    {
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(ConfigPath(name)), Options);
        if (document is not { Version: 1, Settings.Valid: true })
            throw new InvalidDataException("Invalid or unsupported aim configuration.");
        return document.Settings;
    }
}

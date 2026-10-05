using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MystieHook;

public static class Offsets
{
    // Path: EXE directory
    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "offsets.json");

    public sealed class OffsetsFile
    {
        public string process { get; set; } = "example.exe";
        // simple name->hex/dec value dictionary
        public Dictionary<string, string> offsets { get; set; } = new();
        // optional handy groups (purely for UI categorization). key: group, value: list of names
        public Dictionary<string, List<string>> groups { get; set; } = new();
    }

    public static OffsetsFile Data { get; private set; } = new();

    public static void EnsureExistsWithExample()
    {
        if (File.Exists(FilePath)) return;

        var ex = new OffsetsFile
        {
            process = "example.exe",
            offsets = new Dictionary<string, string>
            {
                // pretend addresses/offsets for a typical game layout
                ["World"]        = "0x140F0A8C8",  // absolute RVA from module base
                ["Character"]    = "0x1A0",        // offset from World* (pointer)
                ["Name"]         = "0x2C0",        // offset inside Character
                ["Health"]       = "0x330",
                ["Position"]     = "0x3A0",
                ["Inventory"]    = "0x518",
                ["LocalPlayer"]  = "0x1412345F0"   // another absolute RVA
            },
            groups = new Dictionary<string, List<string>>
            {
                ["Roots"] = new() { "World", "LocalPlayer" },
                ["Character"] = new() { "Character", "Name", "Health", "Position", "Inventory" }
            }
        };

        var json = JsonSerializer.Serialize(ex, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    public static bool Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { EnsureExistsWithExample(); }
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<OffsetsFile>(json);
            if (loaded == null) return false;
            Data = loaded;
            return true;
        }
        catch { return false; }
    }

    public static bool TryGet(string name, out ulong value)
    {
        value = 0;
        if (Data?.offsets == null) return false;
        if (!Data.offsets.TryGetValue(name, out var s)) return false;

        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

        return ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static IEnumerable<string> Names()
        => Data?.offsets != null ? Data.offsets.Keys.AsEnumerable()
                                 : Enumerable.Empty<string>();
}

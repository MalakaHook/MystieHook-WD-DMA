using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImGuiNET;

namespace MystieHook
{
    public static class Config<T> where T : new()
    {
        public static T Settings { get; private set; } = new();

        private static string ConfigDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MystieHook", "DMAExample", "Configs");

        private static JsonSerializerOptions Options => new()
        {
            WriteIndented = true,
            IncludeFields = true
        };

        private static string CurrentConfigName = "default";
        private static bool LoadedOnce = false;

        private static string PathFor(string gameName, string configName) =>
            Path.Combine(ConfigDir, $"{gameName}-{configName}.json");

        private static string LastFileFor(string gameName) =>
            Path.Combine(ConfigDir, $"{gameName}-last.txt");

        public static void Save(string gameName, string configName)
        {
            EnsureDir();
            File.WriteAllText(PathFor(gameName, configName),
                JsonSerializer.Serialize(Settings, Options));

            File.WriteAllText(LastFileFor(gameName), configName); // remember last
        }

        public static bool Load(string gameName, string configName)
        {
            try
            {
                var path = PathFor(gameName, configName);
                if (!File.Exists(path)) return false;

                Settings = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                           ?? new T();

                CurrentConfigName = configName;
                File.WriteAllText(LastFileFor(gameName), configName); // update last
                return true;
            }
            catch { return false; }
        }

        public static IEnumerable<string> ListConfigs(string gameName)
        {
            EnsureDir();
            var prefix = gameName + "-";
            return Directory.GetFiles(ConfigDir, $"{prefix}*.json")
                            .Select(f => Path.GetFileNameWithoutExtension(f)!)
                            .Select(f => f.Substring(prefix.Length));
        }

        public static void Replace(T newSettings) => Settings = newSettings;

        // UI panel for ImGui
        public static void DrawConfigPanel(string gameName, Action<T> drawer, bool embedded = false)
        {
            // auto-load last config on first draw
            if (!LoadedOnce)
            {
                LoadedOnce = true;
                try
                {
                    var lastFile = LastFileFor(gameName);
                    if (File.Exists(lastFile))
                    {
                        var lastCfg = File.ReadAllText(lastFile).Trim();
                        if (!string.IsNullOrWhiteSpace(lastCfg))
                            Load(gameName, lastCfg);
                    }
                    else
                    {
                        Save(gameName, CurrentConfigName);
                    }
                }
                catch { /* ignore */ }
            }
        
            if (!embedded) ImGui.Begin($"{gameName} Settings");
        
            drawer(Settings);
        
            ImGui.Separator();
            ImGui.TextDisabled("Config Management");
        
            ImGui.InputText("Config Name", ref CurrentConfigName, 128);
        
            if (ImGui.Button("Save")) Save(gameName, CurrentConfigName);
            ImGui.SameLine();
            if (ImGui.Button("Load")) Load(gameName, CurrentConfigName);
            ImGui.SameLine();
            if (ImGui.Button("Create New"))
            {
                CurrentConfigName = "newconfig";
                Settings = new T();
                Save(gameName, CurrentConfigName);
            }
        
            ImGui.Separator();
            ImGui.TextDisabled("Available Configs");
        
            // --- Combo for configs ---
            var configs = ListConfigs(gameName).ToList();
            int currentIndex = configs.IndexOf(CurrentConfigName);
            if (currentIndex < 0) currentIndex = 0;
        
            ImGui.PushItemWidth(200);
            if (ImGui.BeginCombo("##ConfigCombo", 
                    configs.Count > 0 ? configs[currentIndex] : "<none>"))
            {
                for (int i = 0; i < configs.Count; i++)
                {
                    bool isSelected = (i == currentIndex);
                    if (ImGui.Selectable(configs[i], isSelected))
                    {
                        CurrentConfigName = configs[i];
                        Load(gameName, CurrentConfigName);
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
        
            ImGui.SameLine();
            if (ImGui.Button("Remove"))
            {
                if (configs.Count > 0)
                {
                    var path = PathFor(gameName, CurrentConfigName);
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                    }
                    catch { /* swallow errors */ }
        
                    // reset to default if deleted current
                    Settings = new T();
                    CurrentConfigName = "default";
                    Save(gameName, CurrentConfigName);
                }
            }
        
            if (!embedded) ImGui.End();
        }


        private static void EnsureDir()
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);
        }
    }
}

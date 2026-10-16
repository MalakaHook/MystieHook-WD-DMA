using System.Text.Json;
using ImGuiNET;
using MystieHook.Gui;
using MystieHook.Services;
using MystieHook.Games.Wardog;
using Raylib_cs;
using static MystieHook.Misc;

namespace MystieHook.Games;

public static partial class GameSelector
{
    private static readonly ConfigurationStore Configurations = new();
    private static string _currentConfig = "Default", _chosenConfig = "Default", _newConfigName = "";
    private static string _configurationStatus = "", _preferredInputPort = "";
    private static string[] _configurationNames = Array.Empty<string>();

    private static UserConfiguration CaptureConfiguration() => new()
    {
        Game = "Wardog",
        Wardog = (GameRegistry.Find("Wardog") as WardogGame)?.CapturePreferences() ?? new(),
        Monitor = _selectedMonitor, VSync = _uiUseVsync, FpsCap = _uiFpsCap,
        Borderless = _applyBorderless, Fullscreen = _applyFullscreen,
        ShowFps = _showFps, SidebarExpanded = _sidebarExpanded,
        VisualPreviewOpen = _previewOpen, FavoriteOptions = MenuTools.Favorites.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
        InputPort = _selectedInputIndex >= 0 && _selectedInputIndex < _inputDevices.Count
            ? _inputDevices[_selectedInputIndex].Port : _preferredInputPort,
        Theme = Theme.Settings.DefaultPreset, Keybinds = _activeProfile
    };

    private static void ApplyConfiguration(UserConfiguration config)
    {
        config.Validate();
        if (!Theme.IsPreset(config.Theme))
            throw new InvalidDataException("This configuration uses an unavailable game or theme.");
        GameRegistry.Select("Wardog");
        (GameRegistry.Find("Wardog") as WardogGame)?.ApplyPreferences(config.Wardog);
        _selectedMonitor = Math.Clamp(config.Monitor, 0, Math.Max(0, Raylib.GetMonitorCount() - 1));
        _uiUseVsync = config.VSync; _uiFpsCap = OverlayWindow.ClampFpsCap(config.FpsCap);
        _applyBorderless = true; _applyFullscreen = false;
        _showFps = config.ShowFps;
        _sidebarExpanded = config.SidebarExpanded;
        _previewOpen = config.VisualPreviewOpen;
        MenuTools.Favorites.Clear();
        foreach (var option in config.FavoriteOptions)
        {
            string migrated=option switch { "6:Keybinds"=>"7:Keybinds", "8:Theme presets"=>"7:Theme presets", _=>option };
            if(MenuTools.Options.Any(o=>o.Id==migrated))MenuTools.Favorites.Add(migrated);
        }
        _preferredInputPort = config.InputPort;
        _selectedInputIndex = _inputDevices.FindIndex(d => d.Port == _preferredInputPort);
        Theme.ApplyPreset(config.Theme);
        _activeProfile = config.Keybinds;
        _hotkeysRegistered = false;
        _editingIndex = -1; _wantsRebind = false;
        OverlayWindowApi.SetFramePacing(_uiUseVsync, _uiFpsCap);
        Misc.ApplyMonitorSelection(_selectedMonitor, _applyBorderless, _applyFullscreen);
        ScreenService.UpdateFromMonitor(_selectedMonitor);
    }

    private static void InitializeConfigurations()
    {
        _activeProfile = new UserConfiguration().Keybinds;
        try
        {
            _configurationNames = Configurations.List();
            if (_configurationNames.Length == 0)
            {
                var config = new UserConfiguration();
                // Import old shortcuts once, preserving customized keys while removing old panels.
                var legacy = Path.Combine(AppContext.BaseDirectory, "Configs", "default.keybinds.json");
                if (File.Exists(legacy))
                {
                    try { config.Keybinds = JsonSerializer.Deserialize<KeybindProfile>(File.ReadAllText(legacy)) ?? config.Keybinds; }
                    catch (JsonException) { }
                }
                UserConfiguration.NormalizeKeybinds(config.Keybinds);
                if (!config.Keybinds.Binds.Any(b => b.Action == "ShowMenu"))
                    config.Keybinds.Binds.Add(new() { Name = "Show menu", Vk = 0x79, Action = "ShowMenu" });
                Configurations.Save("Default", config, create: true);
                _configurationNames = Configurations.List();
            }
            var last = Configurations.LastUsed();
            _chosenConfig = _configurationNames.Contains(last) ? last : _configurationNames[0];
            ApplyChosenConfiguration();
        }
        catch (Exception ex)
        {
            _activeProfile = new UserConfiguration().Keybinds;
            _configurationStatus = "Could not load configuration: " + ex.Message;
        }
    }

    private static void ApplyChosenConfiguration()
    {
        FlushFeatureColors();
        try
        {
            var config = Configurations.Load(_chosenConfig);
            ApplyConfiguration(config);
            _currentConfig = _chosenConfig;
            Configurations.Remember(_currentConfig);
            _configurationStatus = $"Applied {_currentConfig}.";
            MarkConfigurationSaved();
            UiNotifications.Show(_configurationStatus);
        }
        catch (Exception ex) { _configurationStatus = "Could not apply: " + ex.Message; }
    }

    private static void SaveCurrentConfiguration()
    {
        FlushFeatureColors();
        try
        {
            Configurations.Save(_currentConfig, CaptureConfiguration());
            Configurations.Remember(_currentConfig);
            _configurationNames = Configurations.List();
            _configurationStatus = $"Saved {_currentConfig}.";
            MarkConfigurationSaved();
            UiNotifications.Show(_configurationStatus);
        }
        catch (Exception ex) { _configurationStatus = "Could not save: " + ex.Message; }
    }

    private static void DrawConfigurations()
    {
        ImGui.TextUnformatted("Configurations");
        SafeText.Disabled($"Active: {_currentConfig}");
        if (ImGui.BeginCombo("Saved config", _chosenConfig))
        {
            foreach (var name in _configurationNames)
                if (ImGui.Selectable(name, name == _chosenConfig)) _chosenConfig = name;
            ImGui.EndCombo();
        }
        if (ImGui.Button("Apply selected")) ApplyChosenConfiguration();
        ImGui.SameLine();
        if (ImGui.Button("Save current")) SaveCurrentConfiguration();
        ImGui.InputText("New config name", ref _newConfigName, 81);
        if (ImGui.Button("Create from current"))
        {
            FlushFeatureColors();
            try
            {
                var name = _newConfigName.Trim();
                Configurations.Save(name, CaptureConfiguration(), create: true);
                _currentConfig = _chosenConfig = name;
                Configurations.Remember(name);
                _configurationNames = Configurations.List();
                _newConfigName = "";
                _configurationStatus = $"Created {name}.";
                MarkConfigurationSaved();
            UiNotifications.Show(_configurationStatus);
            }
            catch (Exception ex) { _configurationStatus = "Could not create: " + ex.Message; }
        }
        SafeText.Wrapped(_configurationStatus);
        SafeText.Disabled("Save to keep changes. Apply replaces current unsaved options.");
        SafeText.Wrapped(Configurations.DirectoryPath);
    }
}

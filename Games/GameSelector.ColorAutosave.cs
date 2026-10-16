using System.Text.Json;
using ImGuiNET;
using MystieHook.Gui;
using MystieHook.Services;

namespace MystieHook.Games;

public static partial class GameSelector
{
    private static FeatureColorAutosave _colorAutosave;
    private static FeatureColorAutosave ColorAutosave=>_colorAutosave??=new(Configurations);
    private static long _observedColorRevision;
    private static void ProcessColorAutosave()
    {
        if(MenuTools.ColorRevision!=_observedColorRevision)
        {
            _observedColorRevision=MenuTools.ColorRevision;
            ColorAutosave.Request(_currentConfig,FeatureColors.Capture(CaptureConfiguration()),ImGui.GetTime());
        }
        CompleteColorAutosave(ColorAutosave.Tick(ImGui.GetTime(),ImGui.IsMouseDown(ImGuiMouseButton.Left)));
    }
    private static void CompleteColorAutosave(FeatureColorAutosave.Result result)
    {
        if(result==null)return;
        if(result.Error!=null){UiNotifications.Show("Could not auto-save colors: "+result.Error);return;}
        if(result.Profile!=_currentConfig||_savedSnapshot.Length==0)return;
        var saved=JsonSerializer.Deserialize<UserConfiguration>(_savedSnapshot)!;
        result.Colors.Apply(saved);_savedSnapshot=JsonSerializer.Serialize(saved);
        _dirty=JsonSerializer.Serialize(CaptureConfiguration())!=_savedSnapshot;
    }
    internal static void FlushFeatureColors()
    {
        foreach(var result in ColorAutosave.Flush())CompleteColorAutosave(result);
    }
}

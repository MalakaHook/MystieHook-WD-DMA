using ImGuiNET;
using System.Numerics;

namespace MystieHook.Gui;

public static class UiNotifications
{
    private static string _message="";
    private static double _until;
    public static void Show(string message)
    {
        _message=message;
        _until=ImGui.GetTime()+5;
    }
    public static void Draw()
    {
        if(_message.Length==0 || ImGui.GetTime()>_until)return;
        var viewport=ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos+new Vector2(viewport.Size.X-18,18),ImGuiCond.Always,new(1,0));
        ImGui.SetNextWindowSizeConstraints(new(180,0),new(Math.Max(180,Math.Min(420,viewport.Size.X-36)),400));
        ImGui.SetNextWindowBgAlpha(.95f);
        ImGui.Begin("Notification##Qol",ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoInputs|ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoFocusOnAppearing);
        SafeText.Wrapped(_message);
        ImGui.End();
    }
}

using ImGuiNET;
using MystieHook.Gui;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogGame
{
    private string _heldItemLabel="";
    private void DrawHeldItemInspector()
    {
        if(!ImGui.CollapsingHeader("Held item debug",ImGuiTreeNodeFlags.DefaultOpen))return;
        SafeText.Wrapped("Read-only capture of the weapon in your hand. Enter its in-game name so the copied report pairs the name with its identifiers. Capture each weapon separately.");
        ImGui.InputText("In-game weapon name",ref _heldItemLabel,128);
        ImGui.BeginDisabled(!IsAttached);
        if(ImGui.Button("Capture held item"))_monitor.HeldItemInspector.Request(_heldItemLabel);
        ImGui.EndDisabled();
        var inspector=_monitor.HeldItemInspector;
        ImGui.SameLine();
        if(ImGui.Button("Clear capture"))inspector.Clear();
        SafeText.Wrapped(inspector.Status);
        string report=inspector.Report;
        if(report.Length==0)return;
        if(ImGui.Button("Copy weapon report"))ImGui.SetClipboardText(report);
        SafeText.Disabled("This manual report includes item memory identifiers; it is not included in session diagnostics.");
        SafeText.Wrapped(report);
    }
}

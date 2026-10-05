using ImGuiNET;

namespace MystieHook.Gui;

// Text is already formatted by C#. Never pass it to native printf-style APIs.
public static class SafeText
{
    public static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
        try { ImGui.TextUnformatted(text ?? ""); }
        finally { ImGui.PopTextWrapPos(); }
    }

    public static void Disabled(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        try { ImGui.TextUnformatted(text ?? ""); }
        finally { ImGui.PopStyleColor(); }
    }

    public static void Tooltip(string text)
    {
        ImGui.BeginTooltip();
        try { ImGui.TextUnformatted(text ?? ""); }
        finally { ImGui.EndTooltip(); }
    }
}

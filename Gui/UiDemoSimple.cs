using System.Numerics;
using ImGuiNET;
using MystieHook.Games;
using MystieHook.Services;
using static MystieHook.Misc;
using static MystieHook.OverlayWindow;

namespace MystieHook;

public static class ServiceDemoUI
{
    private static string _exe = "example.exe";

    public static void Draw()
    {
        ImGui.PushFont(Fonts.Bold);
        bool open = ImGui.Begin("Service · Control", ImGuiWindowFlags.NoCollapse);
        ImGui.PopFont();
        if (!open) { ImGui.End(); return; }

        var s = Snapshots.Current;

        ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), s.Status);
        ImGui.Separator();

        if (ImGui.Button("Init VMM (no attach)")) VmmService.InitOnly();
        ImGui.SameLine();
        if (ImGui.Button("Dispose VMM")) VmmService.DisposeVmm();

        ImGui.Separator();

        ImGui.InputText("Process", ref _exe, 256);
        if (ImGui.Button("Attach")) VmmService.Attach(_exe);
        ImGui.SameLine();
        if (ImGui.Button("Refresh Processes")) VmmService.RefreshProcesses();

        ImGui.Separator();
        ImGui.TextDisabled($"VMM Ready: {s.VmmReady} | PID: {s.Pid} | Base: 0x{s.MainBase:X}");

        ImGui.Text("Processes:");
        ImGui.BeginChild("proc_child", new Vector2(0, 150), ImGuiChildFlags.None);
        foreach (var p in s.Processes)
            ImGui.TextUnformatted($"{p.Pid,6}  {p.Name}{(p.IsWow64 ? " (Wow64)" : "")}");
        ImGui.EndChild();

        ImGui.Separator();

        if (ImGui.Button("Refresh Modules")) VmmService.RefreshModules();
        ImGui.Text("Modules:");
        ImGui.BeginChild("mods_child", new Vector2(0, 150), ImGuiChildFlags.None);
        foreach (var m in s.Modules)
            ImGui.TextUnformatted($"{m.Name,-28} Base=0x{m.Base:X} Size=0x{m.Size:X}");
        ImGui.EndChild();

        ImGui.End();
    }
}

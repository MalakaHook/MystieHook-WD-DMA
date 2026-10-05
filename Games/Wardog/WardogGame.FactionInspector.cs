using System.Diagnostics;
using ImGuiNET;
using MystieHook.Gui;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogGame
{
    private string _factionCapture = "";
    private void DrawFactionInspector()
    {
        ImGui.SeparatorText("Faction inspector");
        var inspector = _monitor.FactionInspector;
        var selection = inspector.Selection;
        bool enabled = selection.Enabled;
        if (MystieHook.Gui.MenuTools.Checkbox("Enable faction inspector (1 Hz)", ref enabled)) inspector.Select(enabled, selection.Pawn);
        SafeText.Disabled("Optional diagnostic reads. Does not change team filtering. Not saved in configurations.");
        if (!enabled) return;
        var frame = _monitor.Current;
        if (ImGui.BeginCombo("Inspect player", selection.Pawn == 0 ? "Local player" : $"Player 0x{selection.Pawn:X}"))
        {
            if (ImGui.Selectable("Local player", selection.Pawn == 0)) inspector.Select(true, 0);
            foreach (var p in frame.Players)
            {
                ImGui.PushID(p.Address.ToString("X"));
                if (ImGui.Selectable($"{p.Name} ({p.TeamRelation})", selection.Pawn == p.Address)) inspector.Select(true, p.Address);
                ImGui.PopID();
            }
            ImGui.EndCombo();
        }
        var sample = inspector.Current;
        ulong pawn = inspector.Selection.Pawn == 0 ? frame.LocalPawn : inspector.Selection.Pawn;
        if (!WardogReader.InspectionMatches(sample, frame, pawn))
        { SafeText.Wrapped("Waiting for a fresh sample. The selected player must be in the current snapshot."); return; }
        SafeText.Disabled($"Sample age: {Stopwatch.GetElapsedTime(sample.SampleStarted).TotalSeconds:0.0}s");
        foreach (string field in sample.Fields) SafeText.Wrapped(field);
        SafeText.Disabled("Candidate text uses an unverified FText layout. Unresolved does not mean empty.");
        if (ImGui.Button("Copy faction capture"))
        {
            _factionCapture = $"Faction diagnostic | captured {DateTimeOffset.UtcNow:O}\nWorld: 0x{sample.World:X}\nPawn: 0x{sample.Pawn:X}\n" + string.Join("\n", sample.Fields);
            ImGui.SetClipboardText(_factionCapture);
        }
        if (_factionCapture.Length > 0) SafeText.Disabled("Capture copied. Paste it into your notes or this chat.");
    }
}

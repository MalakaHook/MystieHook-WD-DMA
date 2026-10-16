using ImGuiNET;
using MystieHook.Games.Wardog;
using MystieHook.Gui;
using MystieHook.Services;

namespace MystieHook.Games;

public static partial class GameSelector
{
    private static Task<string> _diagnosticExport;
    private static string _diagnosticPath = "", _diagnosticError = "";
    private static void PollDiagnosticsExport()
    {
        if (_diagnosticExport is not { IsCompleted: true }) return;
        try { _diagnosticPath = _diagnosticExport.GetAwaiter().GetResult(); UiNotifications.Show("Diagnostics exported. The path is available in Settings."); }
        catch { _diagnosticError = "Could not write the diagnostics report. Check available space and folder access, then retry."; UiNotifications.Show(_diagnosticError); }
        _diagnosticExport = null;
    }
    private static void DrawDiagnosticsExport(WardogGame game)
    {
        ImGui.SeparatorText("Export diagnostics");
        SafeText.Wrapped("Save a local JSON report with current settings, up to 60 seconds of timing history, connection events and categorized recent errors. Nothing is uploaded.");
        SafeText.Disabled("Excludes license data, device identifiers, names, map points, raw logs and memory addresses.");
        ImGui.BeginDisabled(_diagnosticExport != null);
        if (ImGui.Button(_diagnosticExport == null ? "Export diagnostics" : "Exporting..."))
        {
            _diagnosticError = "";
            try { _diagnosticExport = DiagnosticsExport.WriteAsync(DiagnosticsExport.CaptureSettings(CaptureConfiguration()), game.Diagnostics,
                readStatistics: WardogReadOptimization.Enabled ? game.ReadStatistics : null); }
            catch { _diagnosticError = "Could not capture diagnostics settings. Please retry."; }
        }
        ImGui.EndDisabled();
        if (_diagnosticError.Length > 0) SafeText.Wrapped(_diagnosticError);
        if (_diagnosticPath.Length > 0)
        {
            SafeText.Wrapped(_diagnosticPath);
            if (ImGui.Button("Copy report path")) ImGui.SetClipboardText(_diagnosticPath);
        }
    }
}

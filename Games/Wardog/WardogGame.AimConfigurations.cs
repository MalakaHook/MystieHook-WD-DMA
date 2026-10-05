using MystieHook.Gui;
using ImGuiNET;
using MystieHook.Services;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogGame
{
    private readonly AimConfigurationStore _aimConfigurations = new();
    private sealed record AimConfigResult(string[] Names, WardogAimSettings Loaded, string Selected, string Message);
    private Task<AimConfigResult> _aimConfigTask;
    private bool _aimConfigsInitialized;
    private string[] _aimConfigNames = [];
    private string _aimConfigName = "Default", _aimConfigSelected = "", _aimConfigMessage = "";

    private void DrawAimConfigurations(ref WardogAimSettings settings)
    {
        ImGui.SeparatorText("Aim configurations");
        if (!_aimConfigsInitialized)
        {
            _aimConfigsInitialized = true;
            _aimConfigTask = Task.Run(() => new AimConfigResult(_aimConfigurations.List(), null, "", ""));
        }
        if (_aimConfigTask is { IsCompleted: true })
        {
            try
            {
                var result = _aimConfigTask.GetAwaiter().GetResult();
                _aimConfigNames = result.Names;
                if (result.Selected.Length > 0) _aimConfigSelected = _aimConfigName = result.Selected;
                if (result.Loaded is { } loaded)
                {
                    _aimController.Stop();
                    _calibration.Cancel();
                    _calibrationDraft = _calibrationDraftSource = null;
                    _captureAimKey = false;
                    settings = loaded;
                    _calibrationResetBaseline=loaded.Calibration;
                }
                _aimConfigMessage = result.Message;
            }
            catch (Exception ex) { _aimConfigMessage = "Aim configuration: " + ex.Message; }
            if(_aimConfigMessage.Length>0)UiNotifications.Show(_aimConfigMessage);
            _aimConfigTask = null;
        }
        SafeText.Wrapped("Saves all Aim options, the aim key and calibration. Applying replaces only Aim settings. Saving the same name overwrites that aim configuration.");
        ImGui.BeginDisabled(_aimConfigTask != null || _calibration.Active);
        ImGui.InputText("Aim configuration name", ref _aimConfigName, 49);
        // Finish applying a reviewed calibration before saving to avoid saving the old profile by accident.
        ImGui.BeginDisabled(_calibration.Stage == WardogAimCalibration.Phase.Review);
        if (ImGui.Button("Save aim configuration"))
        {
            string name = _aimConfigName.Trim();
            var snapshot = settings;
            _aimConfigTask = Task.Run(() =>
            {
                _aimConfigurations.Save(name, snapshot);
                return new AimConfigResult(_aimConfigurations.List(), null, name, "Saved " + name + ".");
            });
        }
        ImGui.EndDisabled();
        if (_calibration.Stage == WardogAimCalibration.Phase.Review)
            SafeText.Disabled("Apply or cancel the calibration review before saving.");
        if (ImGui.BeginCombo("Saved aim configurations", _aimConfigSelected.Length == 0 ? "Select..." : _aimConfigSelected))
        {
            foreach (var name in _aimConfigNames)
                if (ImGui.Selectable(name, name == _aimConfigSelected)) _aimConfigSelected = name;
            ImGui.EndCombo();
        }
        ImGui.BeginDisabled(_aimConfigSelected.Length == 0);
        if (ImGui.Button("Apply aim configuration"))
        {
            string name = _aimConfigSelected;
            _aimConfigTask = Task.Run(() => new AimConfigResult(_aimConfigurations.List(), _aimConfigurations.Load(name), name, "Applied " + name + "."));
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Refresh aim configurations"))
            _aimConfigTask = Task.Run(() => new AimConfigResult(_aimConfigurations.List(), null, "", "Aim configurations refreshed."));
        ImGui.EndDisabled();
        if (_aimConfigTask != null) SafeText.Disabled("Working...");
        if (_aimConfigMessage.Length > 0) SafeText.Wrapped(_aimConfigMessage);
        SafeText.Disabled(@"Location: %LOCALAPPDATA%\MystieHook\Aim config");
    }

    private WardogAimProfile _calibrationDraft, _calibrationDraftSource;
    private static WardogAimProfile DrawCalibrationValues(WardogAimProfile profile)
    {
        ImGui.TextUnformatted(profile.LocalMouse ? "Source: DMA PC mouse" : "Source: game camera");
        ImGui.TextUnformatted($"Display: {profile.Width} x {profile.Height} | Reference FOV: {profile.CameraFov:0.##} degrees");
        ImGui.TextUnformatted($"Successful flicks: {profile.SuccessfulFlicks}/8");
        SafeText.Disabled("These are the retained summaries; the complete mouse path is not stored.");
        float duration = profile.DurationMs, deadZone = profile.DeadZonePixels, speed = profile.SpeedDegreesPerSecond;
        float tracking = profile.TrackingErrorDegrees, acquisition = profile.AcquisitionMs;
        int curve = (int)profile.Curve;
        bool changed = MenuTools.Combo("Response curve", ref curve, "Exponential (current)\0Linear\0Ease in\0Ease out\0Smoothstep\0Smootherstep\0");
        changed |= MenuTools.SliderFloat("Response time (ms)", ref duration, 80, 5000, "%.0f", ImGuiSliderFlags.AlwaysClamp);
        DrawAimCurvePreview((WardogAimCurve)curve, duration);
        changed |= MenuTools.SliderFloat("Dead zone (px)", ref deadZone, 1, 12, "%.1f", ImGuiSliderFlags.AlwaysClamp);
        changed |= MenuTools.SliderFloat("Speed limit (degrees/s)", ref speed, 1, 1500, "%.1f", ImGuiSliderFlags.AlwaysClamp);
        if (profile.LocalMouse)
            SafeText.Disabled($"Desktop speed equivalent: {speed * profile.Width / 2 * Math.PI / 180:0} px/s");
        SafeText.Wrapped("Response time can be increased up to 5000 ms, independently of the recorded acquisition time.");
        float error = profile.ErrorPercent;
        changed |= MenuTools.SliderFloat("Response variability / error (%)", ref error, 0, 100, "%.0f%%", ImGuiSliderFlags.AlwaysClamp);
        SafeText.Wrapped("0% keeps the calibrated response. Each aim-key press picks a steady response rate; 100% permits 50% slower to 50% faster response. The speed limit remains a cap. This varies timing, not the selected bone position.");
        ImGui.SeparatorText("Reference measurements");
        changed |= MenuTools.SliderFloat("Tracking RMS error (degrees)", ref tracking, 0, Math.Max(8, profile.TrackingErrorDegrees), "%.3f", ImGuiSliderFlags.AlwaysClamp);
        changed |= MenuTools.SliderFloat("Median acquisition (ms)", ref acquisition, 1, Math.Max(5000, profile.AcquisitionMs), "%.0f", ImGuiSliderFlags.AlwaysClamp);
        SafeText.Wrapped("These two reference values describe the measurement. Editing them saves new reference values but does not change movement; use the response sliders above to tune it.");
        var edited = changed ? profile with { DurationMs = duration, DeadZonePixels = deadZone, SpeedDegreesPerSecond = speed,
            TrackingErrorDegrees = tracking, AcquisitionMs = acquisition, ErrorPercent = error, Curve = (WardogAimCurve)curve } : profile;
        return edited.Valid ? edited : profile;
    }
}

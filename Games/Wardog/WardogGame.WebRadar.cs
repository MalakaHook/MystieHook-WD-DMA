using System.Diagnostics;
using ImGuiNET;
using MystieHook.Gui;
using MystieHook.Services;

namespace MystieHook.Games.Wardog;

public sealed partial class WardogGame
{
    private readonly WebRadarPublisher _webRadar = new();
    private const string RadarServer="http://127.0.0.1:3080";
    private string _radarPassword="";
    private void DrawWebRadar()
    {
        ImGui.TextUnformatted("Web radar");
        SafeText.Wrapped("Open a full-size live map in your browser. Start shares player names, positions, team relationships and vehicle labels with a local server on this computer until stopped.");
        bool running=_webRadar.Running;var status=_webRadar.Status;
        SafeText.Disabled("Runs locally; requires Node.js 22 or newer. No VPS or license required.");
        ImGui.InputText("Viewer password",ref _radarPassword,129,ImGuiInputTextFlags.Password);
        SafeText.Disabled("Minimum 8 characters. Leave empty for link-only access. Passwords are not saved.");
        if(!running)
        {
            if(ImGui.Button("Start web radar"))
                _webRadar.Start(RadarServer,_radarPassword,()=>
                {
                    var frame=_monitor.Current;var settings=Volatile.Read(ref _artillery);
                    int map=settings.AutoMap && WardogArtillerySettings.HasDetectedMap(frame)?frame.DetectedMap:settings.Map;
                    return (frame,_monitor.Vehicles,map);
                },()=>_monitor.Camera);
        }
        else
        {
            if(ImGui.Button("Stop sharing"))_webRadar.Stop();
            ImGui.SameLine();ImGui.BeginDisabled(!status.Active);
            if(ImGui.Button("Apply password"))_webRadar.ChangePassword(_radarPassword);
            ImGui.EndDisabled();
        }
        if(status.Url.Length>0)
        {
            if(ImGui.Button("Open radar")){try{Process.Start(new ProcessStartInfo(status.Url){UseShellExecute=true});}catch{UiNotifications.Show("Could not open browser. Copy the radar link instead.");}}
            ImGui.SameLine();if(ImGui.Button("Copy URL"))ImGui.SetClipboardText(status.Url);
            SafeText.Wrapped(status.Url);
        }
        SafeText.Disabled(status.Message);
        ImGui.Separator();
        bool auto=_artillery.AutoMap;
        if(MenuTools.Checkbox("Detect shared map automatically",ref auto))_artillery=_artillery with {AutoMap=auto};
        int selected=_artillery.Map;
        if(ImGui.Combo("Shared map",ref selected,"Bakurani\0Ozeti\0Zestafona\0"))_artillery=_artillery with {Map=selected,AutoMap=false};
        SafeText.Disabled("The browser includes pan/zoom, player and vehicle filters, and game artillery tables. It does not control the game.");
    }
}

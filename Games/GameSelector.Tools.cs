using System.Text.Json;
using ImGuiNET;
using MystieHook.Gui;
using MystieHook.Games.Wardog;

namespace MystieHook.Games;

public static partial class GameSelector
{
    private static string _savedSnapshot="",_search="",_lastSearch="",_lastNotice="";
    private static bool _dirty;
    private static double _nextDirtyCheck;
    private static MenuTools.Option[] _searchResults=[];
    private static string SearchTabTitle(int tab) => MenuTabs[CurrentMenuTab(tab)];
    private static MenuTools.Option[] SearchMenuOptions(string query)
    {
        query = query.Trim();
        return query.Length == 0 ? [] : MenuTools.Options.Where(o =>
            o.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || o.Help.Contains(query, StringComparison.OrdinalIgnoreCase)
            || SearchTabTitle(o.Tab).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    private static void MarkConfigurationSaved()
    {
        _savedSnapshot=JsonSerializer.Serialize(CaptureConfiguration());
        _dirty=false;
    }
    private static void DrawMenuTools()
    {
        if(ImGui.GetTime()>=_nextDirtyCheck)
        {
            _nextDirtyCheck=ImGui.GetTime()+.3;
            _dirty=JsonSerializer.Serialize(CaptureConfiguration())!=_savedSnapshot;
        }
        ImGui.TextUnformatted(_currentConfig+(_dirty ? "  * Unsaved changes" : "  Saved"));
        ImGui.SameLine();
        if(ImGui.SmallButton("Save##QuickConfig"))SaveCurrentConfiguration();
        ImGui.SameLine();
        ImGui.BeginDisabled(!_dirty || _savedSnapshot.Length==0);
        if(ImGui.SmallButton("Revert##QuickConfig"))
        {
            FlushFeatureColors();
            try
            {
                ApplyConfiguration(JsonSerializer.Deserialize<Services.UserConfiguration>(_savedSnapshot)!);
                _dirty=false;UiNotifications.Show("Reverted to the last saved/applied configuration.");
            }
            catch(Exception ex){UiNotifications.Show("Could not revert: "+ex.Message);}
        }
        ImGui.EndDisabled();
        ImGui.SetNextItemWidth(Math.Max(100,Math.Min(400,ImGui.GetContentRegionAvail().X-50)));
        ImGui.InputTextWithHint("##SearchSettings","Search settings...",ref _search,100);
        ImGui.SameLine();if(ImGui.SmallButton("Clear"))_search="";
        if(_search!=_lastSearch)
        {
            _lastSearch=_search;
            _searchResults=SearchMenuOptions(_search);
        }
        if(_search.Length>0)
        {
            ImGui.BeginChild("SearchResults",new(0,Math.Min(170,Math.Max(35,_searchResults.Length*26))),ImGuiChildFlags.Borders);
            foreach(var option in _searchResults)
                if(ImGui.Selectable(SearchTabTitle(option.Tab)+" / "+option.Title))
                {OpenMenuTab(option.Tab);MenuTools.FocusId=option.Section ? "" : option.Id;
                    if(option.Label=="Visual preview")_previewOpen=true;
                    _search="";break;}
            if(_searchResults.Length==0)SafeText.Disabled("No matching settings.");
            ImGui.EndChild();
        }
    }
    private static void DrawConnectionSummary()
    {
        DrawStatusLight("DMA",DmaMemory.IsVmmReady);ImGui.SameLine();
        DrawStatusLight("Wardog",GameRegistry.Active is WardogGame g && g.IsAttached);ImGui.SameLine();
        DrawStatusLight("Input",Input.InputManager.IsReady);ImGui.SameLine();
        DrawStatusLight("Makcu",Input.Device.connected);
        if(ImGui.SmallButton("Reconnect DMA / game"))
        {if(GameRegistry.Active is WardogGame game)game.Reconnect();UiNotifications.Show("Restarting the Wardog connection...");}
        ImGui.SameLine();
        if(ImGui.SmallButton("Input setup"))OpenMenuTab(5);
    }
    private static void DrawConfigurationNotice()
    {
        if(_configurationStatus!=_lastNotice)
        {
            _lastNotice=_configurationStatus;
            if(_lastNotice.Length>0)UiNotifications.Show(_lastNotice);
        }
        UiNotifications.Draw();
    }
}

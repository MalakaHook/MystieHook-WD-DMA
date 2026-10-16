using ImGuiNET;
using System.Numerics;

namespace MystieHook.Gui;

public static class MenuTools
{
    public sealed record Option(int Tab, string Label, double Default, string Help, bool Section = false)
    {
        public string Id { get; } = Tab + ":" + Label;
        public string Title => Label switch { "##BoxStyle" => "Box style", "##HealthPosition" => "Health bar position", _ => Label };
    }
    public static int Tab { get; set; }
    public static readonly HashSet<string> Favorites = new(StringComparer.Ordinal);
    public static string FocusId { get; set; } = "";
    private static double _highlightUntil;
    private static string _highlight = "";
    public static readonly Option[] Options = BuildOptions();
    private static readonly Dictionary<(int,string),Option> Lookup = Options.ToDictionary(o => (o.Tab,o.Label));
    private static Option[] BuildOptions() =>
    [
        new(0,"Aim configurations",0,"Save or apply calibration and Aim profiles.",true),
        new(1,"Visual preview",0,"Show the character image with boxes, skeleton, name, health and distance.",true),
        new(5,"Connection and input setup",0,"Connect or reconnect DMA, Wardog, InputManager and Makcu.",true),
        new(5,"Performance history",0,"Graphs of reader time, camera/player age and FPS over 30 or 60 seconds.",true),
        new(5,"Connection recovery",0,"Connection progress, automatic retry and last successful data update.",true),
        new(5,"Export diagnostics",0,"Save a local report without license data or device identifiers.",true),
        new(7,"Keybinds",0,"Create and edit the menu and panic shortcuts.",true),
        new(7,"Configurations",0,"Save, apply and create general configurations.",true),
        new(7,"Theme presets",0,"Change menu theme colors using a preset.",true),
        new(3,"Web radar",0,"Share live players and vehicles in your browser.",true),
        new(3,"Detect shared map automatically",1,"Detect the battlefield for web radar while sharing."),
        new(0,"Enable aim",0,"Requires an enabled calibration, fresh target data, the aim key and Makcu."),
        new(0,"Team check",1,"Only confirmed enemies are eligible. Unknown teams are excluded."),
        new(0,"Visible check",0,"Visibility is unsupported. Turning this on blocks targeting."),
        new(0,"Maximum distance (m)",100,"Maximum target distance in metres."),
        new(0,"Aim FOV (px)",80,"Target acquisition radius around the screen centre, in pixels."),
        new(0,"Show FOV",1,"Display the target acquisition circle."),
        new(0,"Target line",0,"Draw a line to the selected target."),
        new(0,"Show prediction",0,"Draw head prediction dots using live bullet speed and an explicit gravity estimate."),
        new(0,"Bullet gravity estimate (cm/s2)",980,"Estimated downward acceleration, not decoded projectile gravity."),
        new(0,"Prediction dot radius",2,"Size of the head prediction dot."),
        new(0,"Show prediction details",0,"Show distance and flight time beside each prediction dot."),
        new(0,"Prediction color",4278242559,"Color of head prediction dots."),
        new(0,"Aim prediction",0,"Aim at the same lead point as the prediction circle. Requires Enable aim and valid calibration."),
        new(0,"Aim Target",0,"Choose closest to crosshair or nearest player within FOV. One player is locked per aim-key press."),
        new(0,"Use calibrated response",1,"Use your applied calibration profile for movement."),
        new(0,"Response time (ms)",300,"Longer durations make the response slower. Milliseconds; independent of the reference measurement."),
        new(0,"Dead zone (px)",3,"Stop correcting when the target is within this many pixels."),
        new(0,"Speed limit (degrees/s)",100,"Upper movement rate, expressed as an angular equivalent. The preview shows the uncapped curve."),
        new(0,"Response variability / error (%)",0,"Varies response timing per aim-key press. Zero preserves the calibrated rate; does not offset the target."),
        new(0,"Tracking RMS error (degrees)",0,"Reference measurement only. Changing this does not change movement."),
        new(0,"Median acquisition (ms)",300,"Reference measurement only. Use Response time to tune movement."),
        new(1,"Enable Player ESP",1,"Enable player drawings from completed local snapshots."),
        new(1,"Show teammates",1,"Include confirmed teammates. Unknown factions remain visible when off."),
        new(1,"Show dead players",1,"Dead players show only their name and a Dead label."),
        new(1,"Show distances",0,"Display distance in metres beneath the player."),
        new(1,"Show Boxes",0,"Display a normal or cornered player box."),
        new(1,"Show bones",0,"Draw skeleton links when a valid pose is available."),
        new(1,"Bone thickness (px)",1.2,"Skeleton line width in pixels. Enable Show bones to edit."),
        new(1,"Head dot",1,"Draw a dot at the head position."),
        new(1,"Head dot radius (px)",3.5,"Head marker radius in pixels. Enable Head dot to edit."),
        new(1,"Player name",1,"Display the cached player name above the player."),
        new(1,"Show faction",0,"Display the faction below the name. Unresolved labels remain Unknown."),
        new(1,"Health bar",1,"Show live health beside the box; choose its side using the adjacent list."),
        new(1,"Show current Item in hand",0,"Display the held-item label beneath the player when available."),
        new(1,"Maximum distance (m)",1000,"Range limit for all player drawings, in metres."),
        new(1,"Player name (px)",14,"Name font size in pixels."),
        new(1,"Player distance (px)",14,"Distance font size in pixels."),
        new(1,"Player faction (px)",14,"Faction font size in pixels."),
        new(2,"Maximum distance (m)",1000,"Vehicle drawing range in metres."),
        new(3,"Range (m)",500,"Radar range, independent of the player overlay range."),
        new(3,"Size (px)",300,"Radar panel size in pixels."),
        new(5,"Show FPS",0,"Display the rendering rate at the top centre of the monitor."),
        new(5,"Read target (Hz)",100,"Requested player update rate. Actual rate depends on read workload."),
        new(0,"Target bone",1,"Choose the body part used by targeting."),
        new(0,"Response curve",0,"How initial movement progresses over time; the chart previews it."),
        new(0,"Target line color",4283971821,"Color of the target indicator line."),
        new(1,"##BoxStyle",0,"Box style: cornered or normal."),
        new(1,"##HealthPosition",0,"Place the health bar on the left, right, top or bottom."),
        new(1,"Box color",4285559039,"Custom box color. The preview updates immediately."),
        new(1,"Bone color",4285559039,"Custom skeleton color."),
        new(1,"Head dot color",4285559039,"Custom head marker color."),
        new(2,"Show Vehicle",0,"Enable vehicle drawings and the vehicle reader when needed."),
        new(2,"Show teammate vehicle",1,"Include vehicles occupied by a confirmed teammate."),
        new(3,"Enable 2D radar",0,"Enable the independent radar view."),
        new(3,"Rotate with camera",1,"Keep the radar oriented with the camera heading."),
        new(3,"Player names",0,"Display player names on radar."),
        new(3,"Vehicles on radar",1,"Include vehicles within radar range."),
        new(5,"Use VSync (syncs to monitor refresh)",0,"Synchronize presentation with the display refresh. Click Apply Frame Pacing afterwards."),
        new(5,"FPS Cap",100,"Maximum rendering frames per second, capped at 100. Zero uses 100. Click Apply Frame Pacing afterwards."),

    ];

    private static bool After(string label, bool canReset)
    {
        Lookup.TryGetValue((Tab,label),out var option);
        if (option == null) return false;
        if (ImGui.IsItemHovered()) SafeText.Tooltip(option.Help + "\nRight-click to reset or pin a shortcut.");
        if (FocusId == option.Id)
        {
            ImGui.SetScrollHereY(.35f); FocusId = "";
            _highlight = option.Id; _highlightUntil = ImGui.GetTime() + 2;
        }
        if (_highlight == option.Id && ImGui.GetTime() < _highlightUntil)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin()-Vector2.One,ImGui.GetItemRectMax()+Vector2.One,ImGui.GetColorU32(ImGuiCol.CheckMark),3,ImDrawFlags.None,2);
        bool reset=false;
        if (ImGui.BeginPopupContextItem(option.Id))
        {
            if (canReset && ImGui.MenuItem("Reset this setting")) reset=true;
            bool pinned=Favorites.Contains(option.Id);
            if (ImGui.MenuItem(pinned ? "Unpin shortcut" : "Pin shortcut in this tab"))
            { if (pinned) Favorites.Remove(option.Id); else Favorites.Add(option.Id); }
            ImGui.EndPopup();
        }
        return reset;
    }
    public static bool Checkbox(string label,ref bool value)
    {
        bool changed=Toggle(label,ref value);
        if(After(label,true)){value=Lookup[(Tab,label)].Default!=0;changed=true;}
        return changed;
    }
    public static bool Toggle(string label,ref bool value)
    {
        var pos=ImGui.GetCursorScreenPos();float h=ImGui.GetFrameHeight(),w=h*1.8f;
        string visible=label.Split("##")[0];var text=ImGui.CalcTextSize(visible);
        bool changed=ImGui.InvisibleButton(label,new Vector2(w+(visible.Length>0?ImGui.GetStyle().ItemInnerSpacing.X+text.X:0),h));
        if(changed)value=!value;
        var draw=ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos,pos+new Vector2(w,h),ImGui.GetColorU32(value?ImGuiCol.CheckMark:ImGuiCol.FrameBg),h/2);
        draw.AddCircleFilled(pos+new Vector2(value?w-h/2:h/2,h/2),h/2-3,ImGui.GetColorU32(ImGuiCol.Text));
        if(visible.Length>0)draw.AddText(pos+new Vector2(w+ImGui.GetStyle().ItemInnerSpacing.X,(h-text.Y)/2),ImGui.GetColorU32(ImGuiCol.Text),visible);
        return changed;
    }
    public static bool SliderFloat(string label,ref float value,float min,float max,string format="%.3f",ImGuiSliderFlags flags=ImGuiSliderFlags.None)
    {
        bool changed=ImGui.SliderFloat(label,ref value,min,max,format,flags|ImGuiSliderFlags.AlwaysClamp);
        if(After(label,true)){value=(float)Math.Clamp(Lookup[(Tab,label)].Default,min,max);changed=true;}
        return changed;
    }
    public static bool SliderInt(string label,ref int value,int min,int max,string format="%d",ImGuiSliderFlags flags=ImGuiSliderFlags.None)
    {
        bool changed=ImGui.SliderInt(label,ref value,min,max,format,flags|ImGuiSliderFlags.AlwaysClamp);
        if(After(label,true)){value=(int)Math.Clamp(Lookup[(Tab,label)].Default,min,max);changed=true;}
        return changed;
    }
    public static bool Combo(string label,ref int value,string items)
    {
        bool changed=ImGui.Combo(label,ref value,items);
        if(After(label,true)){value=(int)Lookup[(Tab,label)].Default;changed=true;}
        return changed;
    }
    public static long ColorRevision {get;private set;}
    public static bool ColorEdit4(string label,ref Vector4 value,ImGuiColorEditFlags flags=ImGuiColorEditFlags.None)
    {
        bool changed=ImGui.ColorEdit4(label,ref value,flags);
        if(After(label,true)){value=ImGui.ColorConvertU32ToFloat4((uint)Lookup[(Tab,label)].Default);changed=true;}
        if(changed)ColorRevision++;
        return changed;
    }
    public static bool InputInt(string label,ref int value)
    {
        bool changed=ImGui.InputInt(label,ref value);
        if(After(label,true)){value=(int)Lookup[(Tab,label)].Default;changed=true;}
        return changed;
    }
    public static void DrawFavorites()
    {
        bool any=false;
        foreach(var option in Options)
        {
            if(option.Tab!=Tab || !Favorites.Contains(option.Id))continue;
            if(!any){ImGui.TextUnformatted("Pinned shortcuts");any=true;}
            if(ImGui.SmallButton(option.Title+"##Pinned")) FocusId=option.Id;
        }
        if(any)ImGui.Separator();
        if(FocusId.Length>0)
        {
            SafeText.Disabled("Finding setting... If hidden, enable its parent option or apply a calibration first.");
            if(ImGui.SmallButton("Clear setting highlight"))FocusId="";
        }
    }
}

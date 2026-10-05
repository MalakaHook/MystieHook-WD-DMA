using System.Numerics;
using ImGuiNET;
using MystieHook.Input;
namespace MystieHook.Games;

public static partial class GameSelector
{
    private static bool _sidebarExpanded = true, _navigationReady;
    private static float _sidebarProgress = 1, _contentProgress = 1;
    private static int _animatedTab = -1;
    private static readonly float[] NavigationHover = new float[11];
    private static string _menuHint = "Menu: F10";
    private static double _nextNavigationHint;
    private static uint NavigationColor(Vector4 color, float alpha=1)
        => ImGui.ColorConvertFloat4ToU32(color with { W=color.W*alpha*ImGui.GetStyle().Alpha });
    private static void DrawNavigation()
    {
        if (!_navigationReady) { _sidebarProgress=_sidebarExpanded ? 1 : 0; _navigationReady=true; }
        float dt=Math.Max(0,ImGui.GetIO().DeltaTime);
        float blend=1-MathF.Exp(-16*dt);
        _sidebarProgress += ((_sidebarExpanded ? 1 : 0)-_sidebarProgress)*blend;
        if (Math.Abs(_sidebarProgress-(_sidebarExpanded ? 1 : 0))<.001f) _sidebarProgress=_sidebarExpanded ? 1 : 0;
        float width=60+160*_sidebarProgress;
        var style=ImGui.GetStyle(); var accent=style.Colors[(int)ImGuiCol.CheckMark];
        var text=style.Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12 + 4 * _sidebarProgress, 12));
        ImGui.BeginChild("Navigation",new Vector2(width,0),ImGuiChildFlags.AlwaysUseWindowPadding,ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();
        if (ImGui.Button(_sidebarExpanded ? "<##Sidebar" : ">##Sidebar",new Vector2(-1,30))) _sidebarExpanded=!_sidebarExpanded;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(_sidebarExpanded ? "Collapse Menu" : "Expand Menu");
        float logoSize=32+16*_sidebarProgress;
        ImGui.SetCursorPosX((width-logoSize)/2);
        MystieHook.Gui.BrandLogo.Draw(logoSize);
        var draw=ImGui.GetWindowDrawList(); var brand=ImGui.GetCursorScreenPos();
        draw.AddText(brand+new Vector2(-8*(1-_sidebarProgress),0),NavigationColor(accent,_sidebarProgress),"M Y S T I E H O O K");
        draw.AddText(brand+new Vector2(-8*(1-_sidebarProgress),22),NavigationColor(style.Colors[(int)ImGuiCol.TextDisabled],_sidebarProgress),"WARDOG EDITION");
        ImGui.Dummy(new Vector2(0,50*_sidebarProgress));
        foreach(int i in MenuTabs.Keys)
        {
            ImGui.PushID(i); var start=ImGui.GetCursorScreenPos();
            float rowWidth=ImGui.GetContentRegionAvail().X;
            bool clicked=ImGui.Button("##Navigation",new Vector2(rowWidth,44));
            bool hover=ImGui.IsItemHovered() || ImGui.IsItemFocused();
            NavigationHover[i] += ((hover ? 1 : 0)-NavigationHover[i])*blend;
            float active=i==_menuTab ? 1 : NavigationHover[i];
            // Cover the stock button with our interpolated theme background.
            draw.AddRectFilled(start,start+new Vector2(rowWidth,44),NavigationColor(style.Colors[(int)ImGuiCol.ChildBg]),style.FrameRounding);
            draw.AddRectFilled(start,start+new Vector2(rowWidth,44),NavigationColor(accent,active),style.FrameRounding);
            var foreground=Vector4.Lerp(style.Colors[(int)ImGuiCol.TextDisabled],text,active);
            DrawNavigationIcon(draw,start+new Vector2(18,22),i,NavigationColor(foreground));
            draw.PushClipRect(start,start+new Vector2(rowWidth,44),true);
            draw.AddText(start+new Vector2(40-8*(1-_sidebarProgress),(44-ImGui.GetFontSize())/2),NavigationColor(foreground,_sidebarProgress),MenuTabs[i]);
            draw.PopClipRect();
            if(hover && _sidebarProgress<.5f) ImGui.SetTooltip(MenuTabs[i]);
            if(clicked) { CloseBindEditor(); _menuTab=i; }
            ImGui.PopID();
        }
        if (ImGui.GetTime()>=_nextNavigationHint)
        {
            _nextNavigationHint=ImGui.GetTime()+1;
            var keys=_activeProfile.Binds.Where(b=>b.Action=="ShowMenu")
                .Select(b=>$"{(b.Ctrl ? "Ctrl+" : "")}{(b.Shift ? "Shift+" : "")}{(b.Alt ? "Alt+" : "")}{VkNames.Name(b.Vk) ?? $"VK {b.Vk:X2}"}").Distinct().ToArray();
            _menuHint="Menu: "+(keys.Length==0 ? "F10" : string.Join(" / ",keys));

        }
        ImGui.SetCursorPosY(Math.Max(ImGui.GetCursorPosY()+12,ImGui.GetWindowHeight()-130));
        ImGui.Separator();
        if (_sidebarProgress>.5f)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha,style.Alpha*_sidebarProgress);
            ImGui.TextWrapped(_menuHint);
            ImGui.PopStyleVar();
        }
        else
        {
            ImGui.InvisibleButton("##SidebarInfo",new Vector2(-1,20));
            if(ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted(_menuHint); ImGui.EndTooltip(); }
        }
        if(ImGui.Button(_sidebarProgress>.5f ? "Hide menu" : "X##HideMenu",new Vector2(-1,32))) ToggleMenu();
        if(ImGui.IsItemHovered()) ImGui.SetTooltip("Hide menu");
        ImGui.EndChild();
    }
    private static void DrawNavigationIcon(ImDrawListPtr d, Vector2 c, int icon, uint color)
    {
        const float t=1.6f;
        switch(icon)
        {
            case 10:
                d.AddLine(c-new Vector2(0,9),c+new Vector2(0,9),color,t);
                d.AddLine(c+new Vector2(0,9),c+new Vector2(-5,3),color,t);
                d.AddLine(c+new Vector2(0,9),c+new Vector2(5,3),color,t);break;
            case 0: d.AddCircle(c,6,color,20,t); d.AddLine(c-new Vector2(11,0),c+new Vector2(11,0),color,t); d.AddLine(c-new Vector2(0,11),c+new Vector2(0,11),color,t); break;
            case 1: d.AddCircle(c,8,color,16,t); d.AddCircleFilled(c,3,color); break;
            case 2:
                d.AddRect(c+new Vector2(-9,-5),c+new Vector2(9,5),color,2,ImDrawFlags.None,t);
                d.AddCircleFilled(c+new Vector2(-5,7),2,color); d.AddCircleFilled(c+new Vector2(5,7),2,color); break;
            case 3:
                d.AddCircle(c,9,color,24,t); d.AddCircle(c,4,color,16,t); d.AddLine(c,c+new Vector2(7,-7),color,t); break;
            case 4: d.AddCircle(c,8,color,24,t); d.AddLine(c-new Vector2(12,0),c+new Vector2(12,0),color,t); d.AddLine(c-new Vector2(0,12),c+new Vector2(0,12),color,t); break;
            case 5:
                for(int n=0;n<3;n++) { float y=-6+n*6; d.AddLine(c+new Vector2(-9,y),c+new Vector2(9,y),color,t); d.AddCircleFilled(c+new Vector2(n==1 ? 4 : -4,y),2.5f,color); } break;
            case 6:
                d.AddRect(c-new Vector2(10,7),c+new Vector2(10,7),color,2,ImDrawFlags.None,t);
                for(int n=0;n<3;n++) d.AddCircleFilled(c+new Vector2(-5+n*5,-2),1.3f,color);
                d.AddLine(c+new Vector2(-5,3),c+new Vector2(5,3),color,t); break;
            case 7:
                d.AddRect(c-new Vector2(8),c+new Vector2(8),color,1,ImDrawFlags.None,t);
                d.AddRect(c+new Vector2(-4,-8),c+new Vector2(4,-2),color,0,ImDrawFlags.None,t);
                d.AddLine(c+new Vector2(-4,4),c+new Vector2(4,4),color,t); break;
            case 8:
                d.AddCircle(c,9,color,24,t); d.AddCircleFilled(c+new Vector2(-3,-3),2,color);
                d.AddCircleFilled(c+new Vector2(4,-2),2,color); d.AddCircleFilled(c+new Vector2(-2,4),2,color); break;
        }
    }
}

using System.Numerics;
using ImGuiNET;
using MystieHook.Gui;
using MystieHook.Games.Wardog;

namespace MystieHook.Games;

public static partial class GameSelector
{
    private static readonly string[] PreviewLetters=["P","R","E","V","I","E","W"];
    private static bool _previewOpen;
    private static float _previewProgress;
    private static Vector2 _menuPosition, _menuSize;
    private static float PreviewWidth => Math.Min(340,Math.Max(180,ImGui.GetIO().DisplaySize.X*.25f));
    private static void AnimatePreview()
    {
        float target=_previewOpen && _menuTab==1 && !_vehicleVisuals ? 1 : 0;
        _previewProgress+=(target-_previewProgress)*(1-MathF.Exp(-14*Math.Max(0,ImGui.GetIO().DeltaTime)));
        if(Math.Abs(_previewProgress-target)<.001f)_previewProgress=target;
    }
    private static void DrawPreviewDrawer()
    {
        if(_menuTab!=1 || _vehicleVisuals)return;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize,new Vector2(1,1));
        var edge=_menuPosition+new Vector2(_menuSize.X,0);
        const ImGuiWindowFlags flags=ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoFocusOnAppearing|ImGuiWindowFlags.NoBringToFrontOnFocus;
        if(_previewProgress>0 && GameRegistry.Active is WardogGame game)
        {
            float width=PreviewWidth, exposed=width*_previewProgress;
            ImGui.SetNextWindowPos(edge);ImGui.SetNextWindowSize(new(exposed,_menuSize.Y));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,Vector2.Zero);
            ImGui.Begin("Visual preview drawer",flags|ImGuiWindowFlags.NoInputs);
            WardogVisualPreview.LoadImage();
            var origin=edge-new Vector2(width-exposed,0);
            var draw=ImGui.GetWindowDrawList();
            draw.PushClipRect(edge,edge+new Vector2(exposed,_menuSize.Y),true);
            draw.AddText(origin+new Vector2(16,12),ImGui.GetColorU32(ImGuiCol.Text),"Preview | 75 HP | 25 m");
            WardogVisualPreview.Draw(draw,origin+new Vector2(0,30),new(width,_menuSize.Y-30),game.PreviewVisuals,game.PreviewEnabled);
            draw.PopClipRect();
            ImGui.End();ImGui.PopStyleVar();
        }
        ImGui.PopStyleVar();
    }
    private static void DrawPreviewHandle()
    {
        if(_menuTab!=1 || _vehicleVisuals)return;
        var edge=_menuPosition+new Vector2(_menuSize.X,0);
        ImGui.SetCursorScreenPos(edge-new Vector2(25,0));
        if(ImGui.InvisibleButton("Toggle preview",new(25,Math.Max(1,_menuSize.Y))))_previewOpen=!_previewOpen;
        bool hover=ImGui.IsItemHovered();
        if(hover)SafeText.Tooltip(_previewOpen ? "Hide visual preview" : "Show visual preview");
        var draw=ImGui.GetWindowDrawList();
        draw.AddRectFilled(edge-new Vector2(25,0),edge+new Vector2(0,_menuSize.Y),ImGui.GetColorU32(ImGuiCol.FrameBg),5);
        uint color=ImGui.GetColorU32(hover || _previewOpen ? ImGuiCol.CheckMark : ImGuiCol.Text);
        float line=ImGui.GetTextLineHeight();
        for(int i=0;i<PreviewLetters.Length;i++)draw.AddText(edge+new Vector2(-18,(_menuSize.Y-PreviewLetters.Length*line)/2+i*line),color,PreviewLetters[i]);
    }
}

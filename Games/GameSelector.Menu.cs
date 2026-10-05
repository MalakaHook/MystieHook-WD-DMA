using System.Numerics;
using ImGuiNET;
using MystieHook.Input;
using MystieHook.Gui;

namespace MystieHook.Games;

public static partial class GameSelector
{
    internal static void PreviewMenuTab(int tab) { _menuTab = CurrentMenuTab(tab); _showFps = true; }
    internal static void PreviewPanic() => RunAction(KeybindAction.Panic);
    private static bool _menuInitialized;
    private static int _menuTab;
    // Keep existing IDs for saved favorites and shortcuts.
    private static readonly Dictionary<int,string> MenuTabs = new()
    { [0]="Aim", [1]="Visuals", [3]="Radar", [5]="Settings", [7]="Config" };
    private static bool _vehicleVisuals;
    private static int CurrentMenuTab(int tab)
    {
        return tab switch {10=>0,2=>1,4=>3,6 or 8=>7,_=>MenuTabs.ContainsKey(tab)?tab:0};
    }

    private static void InitializeMenu()
    {
        if (_menuInitialized) return;
        _menuInitialized = true;
        Theme.ApplyPreset("Wardog Red");
        ImGui.GetIO().ConfigFlags &= ~(ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.ViewportsEnable);
        ScreenService.UpdateFromMonitor(_selectedMonitor);
        GameRegistry.Select("Wardog");
        InitializeConfigurations();
    }

    private static void OpenMenuTab(int tab)
    {
        _menuTab = CurrentMenuTab(tab);
        _allMenusHidden = false;
        UiVisibility.MenusHidden = false;
    }

    private static void ToggleMenu()
    {
        _allMenusHidden = !_allMenusHidden;
        UiVisibility.MenusHidden = _allMenusHidden;
    }

    private static void DrawUnifiedMenu()
    {
        Theme.MatchTabsToAccent();
        AnimatePreview();
        float drawerReserve=_menuTab==1 && !_vehicleVisuals ? PreviewWidth*_previewProgress : 0;
        ImGui.SetNextWindowSize(new Vector2(1200, 800), ImGuiCond.Once);
        ImGui.SetNextWindowPos(new Vector2(40, 45), ImGuiCond.FirstUseEver);
        var display = ImGui.GetIO().DisplaySize;
        var maximum = Vector2.Max(new Vector2(320, 240), display - new Vector2(20+drawerReserve,20));
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(new Vector2(780, 500), maximum), maximum);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(12, 10));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 7));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 7);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);
        // Keep the native resize hit targets without drawing an enclosing panel.
        ImGui.GetIO().ConfigWindowsResizeFromEdges = true;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        bool visible = ImGui.Begin("WARDOG##MainMenu", ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar(); // Child content retains its existing padding.
        if (visible)
        {
            ImGui.SetWindowPos(Vector2.Clamp(ImGui.GetWindowPos(), Vector2.Zero,
                Vector2.Max(Vector2.Zero, display - ImGui.GetWindowSize() - new Vector2(drawerReserve,0))));
            // One shared background joins the children, including during tab slides.
            var menuPosition = ImGui.GetWindowPos();
            _menuPosition=menuPosition;_menuSize=ImGui.GetWindowSize();
            ImGui.GetWindowDrawList().AddRectFilled(menuPosition, menuPosition + ImGui.GetWindowSize(),
                ImGui.GetColorU32(ImGuiCol.ChildBg), ImGui.GetStyle().ChildRounding);
            DrawNavigation();
            ImGui.SameLine(0, 0);
            if (_animatedTab != _menuTab) { _animatedTab = _menuTab; _contentProgress = 0; }
            _contentProgress = Math.Min(1, _contentProgress + Math.Max(0, ImGui.GetIO().DeltaTime) / .18f);
            float contentEase = 1 - MathF.Pow(1 - _contentProgress, 3);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (1-contentEase)*14);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * contentEase);
            ImGui.BeginChild("MainContent", new Vector2(_menuTab==1 && !_vehicleVisuals ? -25 : 0, 0), ImGuiChildFlags.AlwaysUseWindowPadding);
            DrawMenuTools();
            ImGui.TextUnformatted(MenuTabs[_menuTab]);
            ImGui.TextDisabled(_menuTab switch
            {
                0 => "Aim targeting and Makcu input",
                1 => "Player and vehicle visuals",
                2 => "Vehicle visibility and distance",
                3 => "Local radar and browser sharing",
                5 => "Connection, performance and input",

                10 => "Per-weapon vertical pull while firing",
                _ => "Configurations, keybinds and menu theme"
            });
            ImGui.Separator();
            ImGui.BeginChild("Options", new Vector2(0, 0), ImGuiChildFlags.AlwaysUseWindowPadding);
            ImGui.PushItemWidth(230);
            MenuTools.Tab=_menuTab;
            MenuTools.DrawFavorites();
            switch (_menuTab)
            {
                case 10:
                    if(GameRegistry.Active is Wardog.WardogGame rcsGame)rcsGame.DrawRcs();
                    break;
                case 0:
                    if(ImGui.BeginTabBar("AimSections"))
                    {
                        if(ImGui.BeginTabItem("Aim"))
                        { if (GameRegistry.Active is Wardog.WardogGame aimGame) aimGame.DrawAim(); ImGui.EndTabItem(); }
                        if(ImGui.BeginTabItem("RCS"))
                        { if(GameRegistry.Active is Wardog.WardogGame rcsPanel)rcsPanel.DrawRcs(); ImGui.EndTabItem(); }
                        if(ImGui.BeginTabItem("Peripherals"))
                        { DrawInputManager(); ImGui.EndTabItem(); }
                        ImGui.EndTabBar();
                    }
                    break;
                case 1:
                    if(ImGui.BeginTabBar("VisualSections"))
                    {
                        if(ImGui.BeginTabItem("Players"))
                        { _vehicleVisuals=false; if (GameRegistry.Active is Wardog.WardogGame wardog) wardog.DrawVisuals(); else GameHost.Draw(ImGuiWindowFlags.None); ImGui.EndTabItem(); }
                        if(ImGui.BeginTabItem("Vehicles"))
                        { _vehicleVisuals=true; MenuTools.Tab=2; if(GameRegistry.Active is Wardog.WardogGame vehicles)vehicles.DrawVehicles(); MenuTools.Tab=1; ImGui.EndTabItem(); }
                        ImGui.EndTabBar();
                    }
                    break;
                case 2:
                    if (GameRegistry.Active is Wardog.WardogGame vehicleGame) vehicleGame.DrawVehicles();
                    break;
                case 3:
                    if (GameRegistry.Active is Wardog.WardogGame radarGame) radarGame.DrawRadar();
                    break;
                case 5: DrawSettingsContent(); break;
                case 7:
                    DrawConfigurations();
                    ImGui.SeparatorText("Theme");
                    Theme.DrawPresetContent();
                    ImGui.SeparatorText("Keybinds");
                    DrawKeybindsWindow();
                    break;
            }
            ImGui.PopItemWidth();
            ImGui.EndChild();
            ImGui.EndChild();
            ImGui.PopStyleVar();
            DrawPreviewHandle();
        }
        ImGui.End();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(6);
        if(visible)DrawPreviewDrawer();
    }
}

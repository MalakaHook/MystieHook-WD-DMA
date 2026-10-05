using ImGuiNET;

namespace MystieHook.Games
{
    /// <summary>
    /// Tiny glue helpers so OverlayUI (or any UI) can tick/draw the active game.
    /// </summary>
    public static class GameHost
    {
        public static void Tick() => GameRegistry.TickActive();
        public static void Draw(ImGuiWindowFlags winFlags) => GameRegistry.DrawActive(winFlags);
    }
}

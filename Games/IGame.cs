using ImGuiNET;

namespace MystieHook.Games
{
    /// <summary>
    /// A lightweight game plugin contract. Implement this to plug a game's UI/threads
    /// into MystieHook while reusing the shared VMM, JobSystem and window.
    /// </summary>
    public interface IGame
    {
        /// <summary>Short display name for the selector.</summary>
        string Name { get; }

        /// <summary>
        /// Called once on first selection (or app start if you pre-register).
        /// Do NOT block here; offload heavy work to JobSystem, or lazy-start in Start().
        /// </summary>
        void Initialize();

        /// <summary>
        /// Called when the game becomes active. Start per-game workers here.
        /// (Should be idempotent / safe to call if already running.)
        /// </summary>
        void Start();

        /// <summary>
        /// Called when the game is deactivated (or app closes). Stop workers/threads here.
        /// </summary>
        void Stop();

        /// <summary>
        /// Optional per-frame tick for background pumping (lightweight only).
        /// Heavy work should remain on your own threads or JobSystem.
        /// </summary>
        void Tick();

        /// <summary>
        /// Render the game-specific ImGui. Should be fast and UI-only.
        /// </summary>
        void Draw(ImGuiWindowFlags winFlags);
    }
}

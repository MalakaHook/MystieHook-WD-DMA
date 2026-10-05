using System;
using System.Collections.Generic;
using ImGuiNET;

namespace MystieHook.Games
{
    /// <summary>Global registry + active game switch.</summary>
    public static class GameRegistry
    {
        private static readonly Dictionary<string, IGame> _games =
            new(StringComparer.OrdinalIgnoreCase);

        private static IGame? _active;

        public static IGame? Active => _active;
        public static IGame? Find(string name) => _games.TryGetValue(name, out var game) ? game : null;

        public static IEnumerable<string> Names => _games.Keys;

        public static void Register(IGame game)
        {
            if (game == null) return;
            if (_games.ContainsKey(game.Name)) return;
            _games[game.Name] = game;
            // Lazy-init: don't call Initialize here; wait until first select.
        }

        /// <summary>Switch active game. Handles Stop/Start + one-time Initialize.</summary>
        public static void Select(string name)
        {
            if (!_games.TryGetValue(name, out var next)) return;
            if (ReferenceEquals(_active, next)) return;

            try { _active?.Stop(); } catch { /* swallow */ }

            // One-time init only
            try { next.Initialize(); } catch { /* swallow */ }

            _active = next;

            // NOTE: do NOT auto-start here; user will press "Start Workers" in the UI.
        }

        public static void StopActive()
        {
            try { _active?.Stop(); } catch { }
        }

        public static void TickActive() => _active?.Tick();

        public static void DrawActive(ImGuiWindowFlags winFlags) => _active?.Draw(winFlags);
    }
}

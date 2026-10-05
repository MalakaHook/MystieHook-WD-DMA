using System.Text.Json.Serialization;
using MystieHook.Input;
namespace MystieHook.Games;
    public enum KeybindMode { OnPress, OnRelease, WhileDown }

    public enum KeybindAction { ShowMenu, Panic }

    public sealed class KeybindEntry
    {
        public string Name { get; set; } = "New Bind";
        public int Vk { get; set; }
        public bool Ctrl { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public KeybindMode Mode { get; set; } = KeybindMode.OnPress;

        // NOTE: Action is a string so we can store global *or* game-specific actions.
        public string Action { get; set; } = nameof(KeybindAction.ShowMenu);

        public override string ToString()
        {
            string mods = (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "");
            return $"{Name}: {mods}{VkNames.Name(Vk)} ({Mode}) → {Action}";
        }

        public bool MatchesModifiers(bool ctrl, bool shift, bool alt)
            => ctrl == Ctrl && shift == Shift && alt == Alt;
    }

    public sealed class KeybindProfile
    {
        public string ProfileName { get; set; } = "Default";
        public string Category { get; set; } = "Default"; // Default, Wardog, ExampleGame, etc.
        public List<KeybindEntry> Binds { get; set; } = new();
    }


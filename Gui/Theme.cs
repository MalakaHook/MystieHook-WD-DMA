using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImGuiNET;

namespace MystieHook.Gui
{
    public static class Theme
    {
        public sealed class ThemeSettings
        {
            public bool DockingEnable { get; set; } = true;
            public float WindowRounding { get; set; } = 8f;
            public float FrameRounding { get; set; } = 8f;
            public float GrabRounding { get; set; } = 8f;
            public bool AllowResize { get; set; } = true;
            public bool AllowMove { get; set; } = true;
            public bool ShowTitleBar { get; set; } = true;
            public bool NoBackground { get; set; } = false;
            public float GlobalAlpha { get; set; } = 1.0f;
            public float WindowBorderSize { get; set; } = 1.0f;
            public float FrameBorderSize { get; set; } = 0.0f;
            public string DefaultPreset { get; set; } = "Wardog Red";
            public Vector4[] Colors { get; set; } = CaptureColorsFromCurrent();
        }

        public static ThemeSettings Settings { get; private set; } = new();
        private static string ThemeDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MystieHook", "Wardog", "MenuTheme");
        private static string DefaultPath => Path.Combine(ThemeDir, "theme.json");

        private static readonly string[] _presets = new[] { "Wardog Red", "MamboSignature", "Dark", "Classic", "RoyalBlue", "EmeraldGold" };

        // ─────────────────────────────────────────────
        public static void Apply()
        {
            var io = ImGui.GetIO();
            io.ConfigFlags = ImGuiConfigFlags.None;
            // The unified menu stays inside the overlay window.

            var style = ImGui.GetStyle();
            style.WindowRounding = Settings.WindowRounding;
            style.FrameRounding = Settings.FrameRounding;
            style.GrabRounding = Settings.GrabRounding;
            style.Alpha = Settings.GlobalAlpha;
            style.WindowBorderSize = Settings.WindowBorderSize;
            style.FrameBorderSize = Settings.FrameBorderSize;

            var colors = style.Colors;
            for (int i = 0; i < Math.Min((int)ImGuiCol.COUNT, Settings.Colors.Length); i++)
                colors[i] = Settings.Colors[i];
            MatchTabsToAccent();
        }
        public static void MatchTabsToAccent()
        {
            var c=ImGui.GetStyle().Colors;var accent=c[(int)ImGuiCol.CheckMark];var background=c[(int)ImGuiCol.FrameBg];
            c[(int)ImGuiCol.Tab]=background;
            c[(int)ImGuiCol.TabHovered]=Vector4.Lerp(background,accent,.65f);
            c[(int)ImGuiCol.TabSelected]=Vector4.Lerp(background,accent,.45f);
            c[(int)ImGuiCol.TabSelectedOverline]=accent;
            c[(int)ImGuiCol.TabDimmed]=background;
            c[(int)ImGuiCol.TabDimmedSelected]=Vector4.Lerp(background,accent,.25f);
            c[(int)ImGuiCol.TabDimmedSelectedOverline]=accent;
        }

        // ─────────────────────────────────────────────
        public static void Save(string path = null)
        {
            EnsureDir();
            File.WriteAllText(path ?? DefaultPath,
                JsonSerializer.Serialize(Settings, JsonOptions));
        }

        public static bool Load(string path = null)
        {
            try
            {
                path ??= DefaultPath;
                if (!File.Exists(path)) return false;
                Settings = JsonSerializer.Deserialize<ThemeSettings>(
                    File.ReadAllText(path), JsonOptions);
                Apply();
                return true;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────
        public static void ApplyPreset(string name)
        {
            var s = ImGui.GetStyle();
            switch (name)
            {
                case "Wardog Red":
                    ImGui.StyleColorsDark();
                    s.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.043f, 0.051f, 0.055f, 1);
                    s.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.125f, 0.13f, 0.17f, 1);
                    s.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.10f, 0.10f, 0.115f, 1);
                    s.Colors[(int)ImGuiCol.Button] = new Vector4(0.19f, 0.19f, 0.22f, 1);
                    foreach (var color in new[] { ImGuiCol.CheckMark, ImGuiCol.SliderGrab, ImGuiCol.SliderGrabActive, ImGuiCol.ButtonActive, ImGuiCol.Header, ImGuiCol.HeaderActive })
                        s.Colors[(int)color] = new Vector4(0.95f, 0.25f, 0.35f, 1);
                    s.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.70f, 0.22f, 0.30f, 1);
                    s.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.70f, 0.22f, 0.30f, 1);
                    s.Colors[(int)ImGuiCol.Separator] = new Vector4(0.22f, 0.23f, 0.28f, 1);
                    break;
                case "Dark":
                    ImGui.StyleColorsDark();
                    break;
                case "Classic":
                    ImGui.StyleColorsClassic();
                    break;
                case "RoyalBlue":
                    ImGui.StyleColorsDark();
                    s.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.07f, 0.08f, 0.10f, 1f);
                    s.Colors[(int)ImGuiCol.Header] = new Vector4(0.20f, 0.35f, 0.75f, 1f);
                    s.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.28f, 0.46f, 0.95f, 1f);
                    s.Colors[(int)ImGuiCol.Button] = new Vector4(0.15f, 0.25f, 0.55f, 1f);
                    s.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.28f, 0.46f, 0.95f, 1f);
                    s.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.10f, 0.16f, 0.35f, 1f);
                    break;
                case "EmeraldGold":
                    ImGui.StyleColorsDark();
                    s.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.06f, 0.09f, 0.07f, 1f);
                    s.Colors[(int)ImGuiCol.Header] = new Vector4(0.05f, 0.25f, 0.12f, 1f);
                    s.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.10f, 0.40f, 0.20f, 1f);
                    s.Colors[(int)ImGuiCol.Button] = new Vector4(0.08f, 0.30f, 0.15f, 1f);
                    s.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.30f, 0.55f, 0.25f, 1f);
                    s.Colors[(int)ImGuiCol.Text] = new Vector4(0.9f, 0.85f, 0.5f, 1f);
                    s.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.10f, 0.20f, 0.12f, 1f);
                    break;
                case "MamboSignature":
                    ImGui.StyleColorsDark();
                    s.Colors[(int)ImGuiCol.Text] = new Vector4(0.96f, 0.93f, 0.88f, 1f);
                    s.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.55f, 0.5f, 0.45f, 1f);
                    s.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.09f, 0.09f, 0.1f, 1f);
                    s.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.11f, 0.11f, 0.13f, 1f);
                    s.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.1f, 0.1f, 0.12f, 1f);
                    s.Colors[(int)ImGuiCol.Border] = new Vector4(0.25f, 0.22f, 0.18f, 0.4f);
                    s.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0f, 0f, 0f, 0f);
                    s.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.16f, 0.29f, 0.48f, 0.54f);
                    s.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.26f, 0.59f, 0.98f, 0.4f);
                    s.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.26f, 0.59f, 0.98f, 0.67f);
                    s.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.47257382f, 0.25921774f, 0.045861606f, 1f);
                    s.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.75f, 0.45f, 0.1f, 1f);
                    s.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0f, 0f, 0f, 0.51f);
                    s.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.14f, 0.14f, 0.14f, 1f);
                    s.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.02f, 0.02f, 0.02f, 0.53f);
                    s.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.25f, 0.25f, 0.25f, 1f);
                    s.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.5f, 0.35f, 0.25f, 1f);
                    s.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.75f, 0.45f, 0.3f, 1f);
                    s.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.9f, 0.45f, 0.85f, 1f);
                    s.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.9f, 0.7f, 0.25f, 1f);
                    s.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.9f, 0.5f, 0.85f, 1f);
                    s.Colors[(int)ImGuiCol.Button] = new Vector4(0.7f, 0.55f, 0.15f, 1f);
                    s.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.89f, 0.45f, 0.85f, 1f);
                    s.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.92f, 0.6f, 0.25f, 1f);
                    s.Colors[(int)ImGuiCol.Header] = new Vector4(0.5949367f, 0.43018505f, 0f, 1f);
                    s.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.89f, 0.5f, 0.9f, 1f);
                    s.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.93f, 0.62f, 0.4f, 1f);
                    s.Colors[(int)ImGuiCol.Separator] = new Vector4(0.25f, 0.22f, 0.18f, 1f);
                    s.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.95f, 0.5f, 0.85f, 1f);
                    s.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.1f, 0.4f, 0.75f, 1f);
                    s.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.9f, 0.65f, 0.25f, 0.5f);
                    s.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.9f, 0.45f, 0.85f, 0.8f);
                    s.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.95f, 0.65f, 0.3f, 1f);
                    s.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.8059072f, 0.638338f, 0.027203642f, 1f);
                    s.Colors[(int)ImGuiCol.Tab] = new Vector4(0.38818568f, 0.24807143f, 0.03112035f, 1f);
                    s.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.88f, 0.6f, 0.3f, 1f);
                    s.Colors[(int)ImGuiCol.TabSelectedOverline] = new Vector4(0.93f, 0.62f, 0.4f, 1f);
                    s.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.068f, 0.10199998f, 0.14800003f, 0.9724f);
                    s.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.8059072f, 0.638338f, 0.127203642f, 1f);
                    s.Colors[(int)ImGuiCol.TabDimmedSelectedOverline] = new Vector4(0.5f, 0.5f, 0.5f, 0f);
                    s.Colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.26f, 0.59f, 0.98f, 0.7f);
                    s.Colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(0.2f, 0.2f, 0.2f, 1f);
                    s.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.61f, 0.61f, 0.61f, 1f);
                    s.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(1f, 0.43f, 0.35f, 1f);
                    s.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.9f, 0.7f, 0f, 1f);
                    s.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(1f, 0.6f, 0f, 1f);
                    s.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.19f, 0.19f, 0.2f, 1f);
                    s.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.31f, 0.31f, 0.35f, 1f);
                    s.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.23f, 0.23f, 0.25f, 1f);
                    s.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0f, 0f, 0f, 0f);
                    s.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1f, 1f, 1f, 0.06f);
                    s.Colors[(int)ImGuiCol.TextLink] = new Vector4(0.26f, 0.59f, 0.98f, 1f);
                    s.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.26f, 0.59f, 0.98f, 0.35f);
                    s.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1f, 1f, 0f, 0.9f);
                    s.Colors[(int)ImGuiCol.NavCursor] = new Vector4(0.26f, 0.59f, 0.98f, 1f);
                    s.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1f, 1f, 1f, 0.7f);
                    s.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.2f);
                    s.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.35f);

                    s.WindowRounding = 8f;
                    s.FrameRounding = 0f;
                    s.GrabRounding = 8f;
                    s.WindowBorderSize = 1f;
                    s.FrameBorderSize = 0f;
                    Settings.DefaultPreset = "MamboSignature";
                    Settings.Colors = CaptureColorsFromCurrent();
                    Apply();
                    break;
                                  
            }

            Settings.Colors = CaptureColorsFromCurrent();
            Settings.DefaultPreset = name;
            Apply();
        }

        // ─────────────────────────────────────────────
        public static void DrawPresetContent()
        {
            ImGui.TextUnformatted("Color preset");
            ImGui.SetNextItemWidth(260);
            if (ImGui.BeginCombo("##ThemePreset", Settings.DefaultPreset))
            {
                foreach (var preset in _presets)
                {
                    if (ImGui.Selectable(preset, preset == Settings.DefaultPreset))
                    {
                        ApplyPreset(preset);
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.TextDisabled("Save your configuration in Config to keep this preset.");
        }

        public static bool IsPreset(string name) => Array.IndexOf(_presets, name) >= 0;
        private static Vector4[] CaptureColorsFromCurrent()
        {
            var style = ImGui.GetStyle();
            var colors = style.Colors;
            var arr = new Vector4[(int)ImGuiCol.COUNT];
            for (int i = 0; i < arr.Length; i++) arr[i] = colors[i];
            return arr;
        }

        private static void EnsureDir()
        {
            if (!Directory.Exists(ThemeDir))
                Directory.CreateDirectory(ThemeDir);
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new Vector4JsonConverter() }
        };

        private sealed class Vector4JsonConverter : JsonConverter<Vector4>
        {
            public override Vector4 Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
            {
                r.Read(); float x = r.GetSingle();
                r.Read(); float y = r.GetSingle();
                r.Read(); float z = r.GetSingle();
                r.Read(); float w = r.GetSingle();
                r.Read();
                return new Vector4(x, y, z, w);
            }
            public override void Write(Utf8JsonWriter w, Vector4 v, JsonSerializerOptions o)
            {
                w.WriteStartArray();
                w.WriteNumberValue(v.X); w.WriteNumberValue(v.Y);
                w.WriteNumberValue(v.Z); w.WriteNumberValue(v.W);
                w.WriteEndArray();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImGuiNET;
using static MystieHook.Misc;
using static MystieHook.OverlayWindow;

namespace MystieHook;

public static class StyleConfig
{
    // ---------- model ----------
    public sealed class UiSettings
    {
        public bool DockingEnable { get; set; } = true;

        public float WindowRounding { get; set; } = 8f;
        public float FrameRounding  { get; set; } = 8f;
        public float GrabRounding   { get; set; } = 8f;

        public bool AllowResize   { get; set; } = true;
        public bool AllowMove     { get; set; } = true;
        public bool ShowTitleBar  { get; set; } = true;
        public bool NoBackground  { get; set; } = false;

        public float GlobalAlpha      { get; set; } = 1.0f;
        public float WindowBorderSize { get; set; } = 1.0f;
        public float FrameBorderSize  { get; set; } = 0.0f;

        public Vector4[] Colors { get; set; } = CaptureColorsFromCurrent();
    }

    // ---------- state ----------
    public static UiSettings Settings { get; private set; } = new UiSettings();
    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MystieHook", "DMAExample", "Themes");
    public static string DefaultConfigPath => Path.Combine(ConfigDir, "config.json");

    // ---------- apply / capture ----------
    public static void ApplyToImGui(UiSettings s)
    {
        var io = ImGui.GetIO();
        io.ConfigFlags = ImGuiConfigFlags.None;
        if (s.DockingEnable) io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        var style = ImGui.GetStyle();
        style.WindowRounding = s.WindowRounding;
        style.FrameRounding  = s.FrameRounding;
        style.GrabRounding   = s.GrabRounding;
        style.Alpha          = s.GlobalAlpha;
        style.WindowBorderSize = s.WindowBorderSize;
        style.FrameBorderSize  = s.FrameBorderSize;

        var colors = style.Colors;
        int maxCount = (int)ImGuiCol.COUNT;
        int count = Math.Min(maxCount, s.Colors.Length);
        for (int i = 0; i < count; i++)
            colors[i] = s.Colors[i];
    }

    public static UiSettings CaptureFromImGui()
    {
        var io = ImGui.GetIO();
        var st = ImGui.GetStyle();

        return new UiSettings
        {
            DockingEnable = (io.ConfigFlags & ImGuiConfigFlags.DockingEnable) != 0,
            WindowRounding = st.WindowRounding,
            FrameRounding  = st.FrameRounding,
            GrabRounding   = st.GrabRounding,
            GlobalAlpha    = st.Alpha,
            WindowBorderSize = st.WindowBorderSize,
            FrameBorderSize  = st.FrameBorderSize,
            Colors = CaptureColorsFromCurrent(),
            AllowResize = Settings.AllowResize,
            AllowMove   = Settings.AllowMove,
            ShowTitleBar = Settings.ShowTitleBar,
            NoBackground = Settings.NoBackground
        };
    }

    private static Vector4[] CaptureColorsFromCurrent()
    {
        var style = ImGui.GetStyle();
        var colors = style.Colors;
        int count = (int)ImGuiCol.COUNT;

        var arr = new Vector4[count];
        for (int i = 0; i < count; i++) arr[i] = colors[i];
        return arr;
    }

    public static ImGuiWindowFlags MakeWindowFlags()
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.None;
        if (!Settings.AllowResize)  flags |= ImGuiWindowFlags.NoResize;
        if (!Settings.AllowMove)    flags |= ImGuiWindowFlags.NoMove;
        if (!Settings.ShowTitleBar) flags |= ImGuiWindowFlags.NoTitleBar;
        if (Settings.NoBackground)  flags |= ImGuiWindowFlags.NoBackground;
        return flags;
    }

    // ---------- persistence ----------
    public static void Save(string nameOrPath = null)
    {
        EnsureDir();
        var path = ResolvePath(nameOrPath);
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static bool Load(string nameOrPath = null)
    {
        try
        {
            var path = ResolvePath(nameOrPath);
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<UiSettings>(json, JsonOptions);
            if (loaded == null) return false;
            Settings = loaded;
            ApplyToImGui(Settings);
            return true;
        }
        catch { return false; }
    }

    public static IEnumerable<string> ListConfigs()
    {
        EnsureDir();
        foreach (var f in Directory.GetFiles(ConfigDir, "*.json"))
            yield return f;
    }

    public static void CopyToClipboard()
    {
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        ImGui.SetClipboardText(json);
    }

    public static bool LoadFromClipboard()
    {
        try
        {
            var txt = ImGui.GetClipboardText();
            if (string.IsNullOrWhiteSpace(txt)) return false;
            var loaded = JsonSerializer.Deserialize<UiSettings>(txt, JsonOptions);
            if (loaded == null) return false;
            Settings = loaded;
            ApplyToImGui(Settings);
            return true;
        }
        catch { return false; }
    }

    private static void EnsureDir()
    {
        if (!Directory.Exists(ConfigDir))
            Directory.CreateDirectory(ConfigDir);
    }

    private static string ResolvePath(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
            return DefaultConfigPath;

        if (Path.IsPathRooted(nameOrPath) || nameOrPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return nameOrPath;

        return Path.Combine(ConfigDir, nameOrPath + ".json");
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
            if (r.TokenType != JsonTokenType.StartArray) throw new JsonException();
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
            w.WriteNumberValue(v.X);
            w.WriteNumberValue(v.Y);
            w.WriteNumberValue(v.Z);
            w.WriteNumberValue(v.W);
            w.WriteEndArray();
        }
    }

    public static void Replace(UiSettings newSettings)
    {
        Settings = newSettings;
        ApplyToImGui(Settings);
    }
}

// ---------- UI panel ----------
public static class StyleEditorUI
{
    private static string _saveName = "config";

    public static void Draw()
    {
        ImGui.PushFont(Fonts.Bold);
        bool open = ImGui.Begin("MystieHook · Settings");
        ImGui.PopFont();
        if (!open) { ImGui.End(); return; }

        var s = StyleConfig.Settings;

        if (ImGui.BeginTabBar("SettingsTabs"))
        {
            // ---------------- Style ----------------
            if (ImGui.BeginTabItem("Style"))
            {
                // Flags (locals then write-back)
                bool docking = s.DockingEnable;
                if (ImGui.Checkbox("Docking", ref docking)) s.DockingEnable = docking;

                bool allowResize = s.AllowResize;
                if (ImGui.Checkbox("Allow Resize", ref allowResize)) s.AllowResize = allowResize;

                UiLayout.SameLineIfFits(UiLayout.CalcButtonSize("Allow Move").X + 30);
                bool allowMove = s.AllowMove;
                if (ImGui.Checkbox("Allow Move", ref allowMove)) s.AllowMove = allowMove;

                UiLayout.SameLineIfFits(UiLayout.CalcButtonSize("Show TitleBar").X + 30);
                bool showTitle = s.ShowTitleBar;
                if (ImGui.Checkbox("Show TitleBar", ref showTitle)) s.ShowTitleBar = showTitle;

                UiLayout.SameLineIfFits(UiLayout.CalcButtonSize("No Background").X + 30);
                bool noBg = s.NoBackground;
                if (ImGui.Checkbox("No Background", ref noBg)) s.NoBackground = noBg;

                ImGui.Separator();

                // Sliders: clamp width (NOT full-width)
                using (UiLayout.PushFieldWidth())
                {
                    float winRound = s.WindowRounding;
                    if (ImGui.SliderFloat("Window Rounding", ref winRound, 0f, 20f)) s.WindowRounding = winRound;
                }
                using (UiLayout.PushFieldWidth())
                {
                    float frameRound = s.FrameRounding;
                    if (ImGui.SliderFloat("Frame Rounding", ref frameRound, 0f, 20f)) s.FrameRounding = frameRound;
                }
                using (UiLayout.PushFieldWidth())
                {
                    float grabRound = s.GrabRounding;
                    if (ImGui.SliderFloat("Grab Rounding", ref grabRound, 0f, 20f)) s.GrabRounding = grabRound;
                }
                using (UiLayout.PushFieldWidth())
                {
                    float alpha = s.GlobalAlpha;
                    if (ImGui.SliderFloat("Global Alpha", ref alpha, 0.2f, 1f)) s.GlobalAlpha = alpha;
                }
                using (UiLayout.PushFieldWidth())
                {
                    float winBorder = s.WindowBorderSize;
                    if (ImGui.SliderFloat("Window Border", ref winBorder, 0f, 4f)) s.WindowBorderSize = winBorder;
                }
                using (UiLayout.PushFieldWidth())
                {
                    float frameBorder = s.FrameBorderSize;
                    if (ImGui.SliderFloat("Frame Border", ref frameBorder, 0f, 4f)) s.FrameBorderSize = frameBorder;
                }

                ImGui.NewLine();

                if (UiLayout.ButtonAuto("Apply"))
                    StyleConfig.ApplyToImGui(s);
                if (UiLayout.ButtonAuto("Capture from Current"))
                    StyleConfig.Replace(StyleConfig.CaptureFromImGui());

                ImGui.Separator();

                // Colors
                var styleColors = ImGui.GetStyle().Colors;
                int colorCount = (int)ImGuiCol.COUNT;

                ImGui.BeginChild("ColorsChild", new Vector2(0, 280), ImGuiChildFlags.None);
                for (int i = 0; i < colorCount; i++)
                {
                    string name = ImGui.GetStyleColorName((ImGuiCol)i);
                    var col = s.Colors[i];
                    if (ImGui.ColorEdit4(name, ref col, ImGuiColorEditFlags.NoInputs))
                    {
                        s.Colors[i]    = col;
                        styleColors[i] = col; // live apply
                    }
                }
                ImGui.EndChild();

                ImGui.EndTabItem();
            }

            // ---------------- Configs ----------------
            if (ImGui.BeginTabItem("Configs"))
            {
                using (UiLayout.PushFieldWidth())
                {
                    ImGui.InputText("File name", ref _saveName, 128);
                }

                if (UiLayout.ButtonAuto("Save")) StyleConfig.Save(_saveName);
                if (UiLayout.ButtonAuto("Load")) StyleConfig.Load(_saveName);
                if (UiLayout.ButtonAuto("Copy to Clipboard")) StyleConfig.CopyToClipboard();
                if (UiLayout.ButtonAuto("Load from Clipboard")) StyleConfig.LoadFromClipboard();

                ImGui.Separator();

                if (ImGui.Button("Refresh List")) { /* list is live */ }

                ImGui.BeginChild("ConfigList", new Vector2(0, 240), ImGuiChildFlags.None);
                foreach (var path in StyleConfig.ListConfigs())
                {
                    string file = Path.GetFileName(path);
                    ImGui.BulletText(file);

                    if (UiLayout.ButtonAuto($"Load##{file}")) StyleConfig.Load(path);
                    if (UiLayout.ButtonAuto($"Save As##{file}")) StyleConfig.Save(file);
                    if (UiLayout.ButtonAuto($"Copy Path##{file}")) ImGui.SetClipboardText(path);
                }
                ImGui.EndChild();

                ImGui.TextDisabled($"Config dir: {StyleConfig.ConfigDir}");

                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    internal static class UiLayout
    {
        /// Place next item on the same row if it fits; otherwise let it wrap.
        public static void SameLineIfFits(float nextWidth, float spacingX = -1f)
        {
            float avail = ImGui.GetContentRegionAvail().X;
            if (nextWidth <= avail)
                ImGui.SameLine(0, spacingX < 0 ? ImGui.GetStyle().ItemSpacing.X : spacingX);
        }

        /// Calculate typical button size (text + frame padding).
        public static Vector2 CalcButtonSize(string label)
        {
            var style = ImGui.GetStyle();
            var text = ImGui.CalcTextSize(label);
            return new Vector2(
                text.X + style.FramePadding.X * 2f,
                text.Y + style.FramePadding.Y * 2f
            );
        }

        /// Button that auto-wraps to the next line when it won’t fit.
        public static bool ButtonAuto(string label)
        {
            var size = CalcButtonSize(label);
            SameLineIfFits(size.X);
            return ImGui.Button(label);
        }

        /// Push a sane field width: clamp to [minWidth, maxWidth] and not larger than available.
        public static IDisposable PushFieldWidth(float minWidth = 60f, float maxWidth = 220f)
        {
            float avail = ImGui.GetContentRegionAvail().X;
            float width = MathF.Min(MathF.Max(minWidth, avail), maxWidth);
            ImGui.PushItemWidth(width);
            return new PopWidthScope();
        }
        private sealed class PopWidthScope : IDisposable { public void Dispose() => ImGui.PopItemWidth(); }

        /// Draw several buttons on one row, wrapping to the next line as needed.
        /// Returns the index of the clicked button, or -1 if none clicked.
        public static int ButtonRowAuto(params string[] labels)
        {
            int hit = -1;
            for (int i = 0; i < labels.Length; i++)
            {
                var size = CalcButtonSize(labels[i]);
                SameLineIfFits(size.X);
                if (ImGui.Button(labels[i]))
                    hit = (hit == -1) ? i : hit;
            }
            return hit;
        }
    }
}

using System;
using System.Numerics;
using System.Threading;
using ImGuiNET;
using MystieHook.Services;
using static MystieHook.OverlayWindow;
using static MystieHook.Misc;
using Raylib_cs;

namespace MystieHook.Games.Example
{
    /// <summary>
    /// A standalone demo game for testing config and overlay rendering.
    /// </summary>
    public sealed class ExampleGame : IGame
    {
        public string Name => "ExampleGame";

        private bool _initialized;
        private bool _running;

        private static ExampleConfig Cfg => Config<ExampleConfig>.Settings;

        private static ExampleEntity[] _entities = Array.Empty<ExampleEntity>();
        private static readonly Random _rng = new();

        public void Initialize()
        {
            if (_initialized) return;

            // initialize screen service from current monitor if not done yet
            if (ScreenService.Current.W <= 0 || ScreenService.Current.H <= 0)
                ScreenService.UpdateFromMonitor(GameSelector.SelectedMonitor);

            _initialized = true;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;

            // fake worker: spawn demo entities
            new Thread(() =>
            {
                while (_running)
                {
                    var ents = new ExampleEntity[10];
                    for (int i = 0; i < ents.Length; i++)
                    {
                        ents[i] = new ExampleEntity
                        {
                            Name = $"Entity_{i}",
                            Distance = _rng.Next(5, 500),
                            // random position relative to current screen size
                            Pos = new Vector3(
                                _rng.Next(50, (int)ScreenService.Current.W - 50),
                                _rng.Next(50, (int)ScreenService.Current.H - 50),
                                0
                            )
                        };
                    }
                    Volatile.Write(ref _entities, ents);
                    Thread.Sleep(1000);
                }
            })
            { IsBackground = true }.Start();
        }

        public void Stop() => _running = false;

        public void Tick() { /* nothing */ }

        public void Draw(ImGuiWindowFlags winFlags)
        {
            if (UiVisibility.MenusHidden) return;
            Config<ExampleConfig>.DrawConfigPanel(Name, cfg =>
            {
                var ready = _running;
                var color = ready ? new Vector4(0, 0.8f, 0, 1) : new Vector4(1f, 0.3f, 0.2f, 1);
                DrawStatusInline(color, ready ? "Running (demo entities)" : "Click Start Workers");
                ImGui.Separator();

                ImGui.Checkbox("Draw Boxes", ref cfg.DrawBoxes);
                ImGui.Checkbox("Show Name", ref cfg.ShowName);
                ImGui.Checkbox("Show Distance", ref cfg.ShowDistance);

                ImGui.SliderFloat("Max Draw Distance", ref cfg.MaxDrawDistance, 50f, 2000f);
                ImGui.ColorEdit4("Box Color", ref cfg.BoxColor);
                ImGui.ColorEdit4("Name Color", ref cfg.NameColor);
                ImGui.ColorEdit4("Distance Color", ref cfg.DistanceColor);

                ImGui.Separator();
                if (ImGui.Button(_running ? "Restart Workers" : "Start Workers"))
                {
                    if (_running) { Stop(); Start(); }
                    else Start();
                }
                ImGui.SameLine();
                if (ImGui.Button("Stop Workers")) Stop();
            }, embedded: true);


        }

        private static void DrawStatusInline(Vector4 color, string caption)
        {
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            float y = p.Y + ImGui.GetTextLineHeight() * 0.5f;
            dl.AddCircleFilled(new Vector2(p.X + 5, y), 5, ImGui.ColorConvertFloat4ToU32(color));
            ImGui.Dummy(new Vector2(14, ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            ImGui.TextDisabled(caption);
        }

        public static void DrawEntitiesOverlay()
        {
            var ents = Volatile.Read(ref _entities);
            if (ents == null || ents.Length == 0) return;

            var dl = ImGui.GetForegroundDrawList();
            var cfg = Cfg;

            foreach (var e in ents)
            {
                if (e.Distance > cfg.MaxDrawDistance) continue;

                if (cfg.DrawBoxes)
                {
                    var min = new Vector2(e.Pos.X - 20, e.Pos.Y - 20);
                    var max = new Vector2(e.Pos.X + 20, e.Pos.Y + 20);
                    dl.AddRect(min, max, ImGui.GetColorU32(cfg.BoxColor));
                }

                if (cfg.ShowName)
                    dl.AddText(new Vector2(e.Pos.X, e.Pos.Y - 30),
                               ImGui.GetColorU32(cfg.NameColor), e.Name);

                if (cfg.ShowDistance)
                    dl.AddText(new Vector2(e.Pos.X, e.Pos.Y + 30),
                               ImGui.GetColorU32(cfg.DistanceColor), $"{e.Distance}m");
            }
        }

        private sealed class ExampleEntity
        {
            public string Name;
            public int Distance;
            public Vector3 Pos;
        }
    }
}

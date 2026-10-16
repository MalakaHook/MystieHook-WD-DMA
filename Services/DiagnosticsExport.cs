using System.Text.Json;
using System.Text.Json.Serialization;
using MystieHook.Games;
using MystieHook.Games.Wardog;

namespace MystieHook.Services;

public static class DiagnosticsExport
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MystieHook", "Diagnostics");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    // Called once on click to detach the report from mutable UI settings. No file or hardware access.
    public static JsonElement CaptureSettings(UserConfiguration c)
    {
        var w = c.Wardog;
        return JsonSerializer.SerializeToElement(new
        {
            Display = new { c.Monitor, c.VSync, c.FpsCap, c.Borderless, c.Fullscreen, c.ShowFps, c.SidebarExpanded, c.VisualPreviewOpen },
            w.Overlay, w.ReadHz, w.Visuals, w.Vehicles, w.Radar,
            Artillery = new { w.Artillery.Map, w.Artillery.Weapon, w.Artillery.Mode, w.Artillery.Players, w.Artillery.Vehicles, w.Artillery.Locations, w.Artillery.Zoom },
            Aim = new { w.Aim.Enabled, w.Aim.UseCalibration, w.Aim.Calibration, w.Aim.Fov, w.Aim.Distance, w.Aim.Bone,
                w.Aim.Key, w.Aim.TeamCheck, w.Aim.TargetLine, w.Aim.ShowFov, w.Aim.LineColor },
            // Omit custom names, process/port strings, map coordinates, saved points and all licensing/device data.
            Keybinds = c.Keybinds.Binds.Take(128).Select(b => new { b.Vk, b.Ctrl, b.Shift, b.Alt, b.Mode,
                Action = Enum.TryParse<KeybindAction>(b.Action, out var action) && Enum.IsDefined(action) ? action.ToString() : "Unknown" }).ToArray()
        }, Options);
    }

    public static Task<string> WriteAsync(JsonElement settings, SessionDiagnostics history, string directory = null,
        WardogReadStatistics.Sample[] readStatistics = null)
    {
        // Copy bounded UI-owned buffers before dispatch; the worker never accesses live game/UI objects.
        var report = new
        {
            FormatVersion = 2, CreatedUtc = DateTimeOffset.UtcNow,
            BuildVariant = WardogReadOptimization.Enabled ? "GitHub-ReadOptimization" : "GitHub-Baseline", DisplayPolicy = "Independent priority camera targeting 120 Hz, latest matching camera without interpolation. Player world poses use 25ms interpolation. Camera/player freshness remains 100/250ms from original acquisition. Current-scan page caching and persistent bounded scatter; original production bone reader.",
            ApplicationVersion = typeof(DiagnosticsExport).Assembly.GetName().Version?.ToString(),
            Runtime = Environment.Version.ToString(), Is64BitProcess = Environment.Is64BitProcess,
            Notes = "Local report. No raw logs, player data, license data, device identifiers, memory addresses or user paths. Samples are 100 ms buckets: peak times, minimum FPS; unavailable frames include disconnected/context-mismatched frames. Gaps mean no UI observation, not zero latency. Worker timings describe completed operations and can repeat while a worker is blocked. Native transfer time includes any internal library waits; gate wait is measured separately. Longest transfer kind belongs to the longest duration; other peaks need not be from the same cycle.",
            VmmRefresh = VmmRefreshExperiment.Current,
#if WARDOG_READ_OPTIMIZATION
            ReadOptimization = new { Enabled = true, Statistics = readStatistics?.ToArray() ?? [],
                Notes = "Cumulative requested bytes/ranges and virtual page visits since attachment; not measured physical DMA throughput. p95 covers the last 1024 completed transfers per reader. Timing includes native-library work; gate wait is separate." },
#endif
            Settings = settings, Latest = history.Latest,
            history.LastReadySeconds, history.LastPlayerSampleSeconds, history.LastCameraSampleSeconds,
            Performance = history.CopyPoints(), ConnectionEvents = history.CopyEvents(), RecentErrors = DiagnosticErrors.Snapshot()
        };
        string target = directory ?? DirectoryPath;
        return Task.Run(async () =>
        {
            Directory.CreateDirectory(target);
            string path = Path.Combine(target, $"diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            string temporary = path + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, true))
                    await JsonSerializer.SerializeAsync(stream, report, Options).ConfigureAwait(false);
                File.Move(temporary, path);
                return path;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        });
    }
}

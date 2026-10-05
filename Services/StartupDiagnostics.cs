using System.Runtime.InteropServices;

namespace MystieHook.Services;

internal static class StartupDiagnostics
{
    public static string LogPath { get; private set; } = "";
    private static readonly object Sync = new();

    public static void Initialize()
    {
        foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.GetTempPath() })
        {
            try
            {
                string dir = Path.Combine(root, "MystieHook", "Logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"startup-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
                File.WriteAllText(path, "Wardog startup diagnostics" + Environment.NewLine);
                LogPath = path;
                break;
            }
            catch { }
        }
        Record($"OS: {RuntimeInformation.OSDescription}; process: {RuntimeInformation.ProcessArchitecture}; runtime: {RuntimeInformation.FrameworkDescription}");
        Record($"Application directory: {AppContext.BaseDirectory}; working directory: {Environment.CurrentDirectory}");
        Record("Log: " + LogPath);
    }

    public static void Record(string message)
    {
        DiagnosticErrors.Record(message);
        string line = $"[{DateTime.Now:O}] {message}";
        Console.Error.WriteLine(line);
        lock (Sync)
        {
            try { if (LogPath.Length > 0) File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch { }
        }
    }

    public static void CheckAssets()
    {
        foreach (string file in new[] { "Assets/Img/Logo.png", "Assets/Img/Logo.ico", "Assets/Fonts/AlanSans-Medium.ttf", "Assets/Fonts/AlanSans-SemiBold.ttf", "Assets/Fonts/AlanSans-Bold.ttf" })
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, file)))
                throw new FileNotFoundException("Application files are missing. Extract and copy the entire portable folder, including Assets.", file);
    }

    public static void CheckNativeLibraries()
    {
        foreach (string file in new[] { "raylib.dll", "cimgui.dll", "libSkiaSharp.dll", "vmm.dll", "leechcore.dll", "FTD3XX.dll" })
        {
            Record("Checking native dependency: " + file);
            string path = Path.Combine(AppContext.BaseDirectory, file);
            if (!File.Exists(path)) throw new FileNotFoundException("Missing native dependency: " + file, path);
            // Load only: do not initialize VMM or connect to a DMA device.
            nint handle = NativeLibrary.Load(path);
            NativeLibrary.Free(handle);
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace MystieHook.Services
{
    /// <summary>
    /// Global thread-safe logger with color-coded console output and optional file logging.
    /// Usage:
    ///     Logger.Info("Game started");
    ///     Logger.Warn("DMA not attached");
    ///     Logger.Debug($"Player count = {count}");
    /// </summary>
    public static class Logger
    {
        private static readonly object _sync = new();
        private static readonly ConcurrentQueue<(string Level, string Message, ConsoleColor Color)> _queue = new();
        private static string _logFile = "";
        private static Thread? _worker;
        private static volatile bool _running;

        public static bool WriteToFile { get; set; } = true;
        public static bool EnableDebug { get; set; } = true;

        static Logger()
        {
            // Logging must never poison this type and abort an attachment warning path.
            // File access belongs to the worker, not the caller or static initializer.
            try { StartBackgroundWriter(); }
            catch { _running = false; }
        }

        internal static string TryCreateLogFile(IEnumerable<string> roots)
        {
            foreach (string root in roots)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    string directory = Path.Combine(root, "MystieHook", "Logs");
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, $"log_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}_{Guid.NewGuid():N}.txt");
                    // Probe the file too: an existing directory may not be writable.
                    using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
                    return path;
                }
                catch { /* Try the next location; no file is also a valid outcome. */ }
            }
            return "";
        }

        private static string InitializeLogFile()
        {
            string local = "", temporary = "";
            try { local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } catch { }
            try { temporary = Path.GetTempPath(); } catch { }
            return TryCreateLogFile(new[] { local, temporary });
        }

        // ��������������������������������������������������������������������������������������������������������������������������
        // Main API
        // ��������������������������������������������������������������������������������������������������������������������������
        public static void Info(string msg)  => Enqueue("INFO", msg, ConsoleColor.Cyan);
        public static void Warn(string msg)  => Enqueue("WARN", msg, ConsoleColor.Yellow);
        public static void Error(string msg) => Enqueue("ERROR", msg, ConsoleColor.Red);
        public static void Debug(string msg)
        {
            if (EnableDebug)
                Enqueue("DEBUG", msg, ConsoleColor.Gray);
        }

        // ��������������������������������������������������������������������������������������������������������������������������
        // Internal queue + worker
        // ��������������������������������������������������������������������������������������������������������������������������
        private static void Enqueue(string level, string msg, ConsoleColor color)
        {
            if (_running && _queue.Count < 4096) _queue.Enqueue((level, msg, color));
        }

        private static void StartBackgroundWriter()
        {
            if (_running) return;
            _running = true;
            _worker = new Thread(() =>
            {
                _logFile = InitializeLogFile();
                while (_running)
                {
                    if (_queue.TryDequeue(out var entry))
                    {
                        Write(entry.Level, entry.Message, entry.Color);
                    }
                    else Thread.Sleep(4);
                }
            })
            { IsBackground = true, Name = "LoggerWorker" };
            _worker.Start();
        }

        private static void Write(string level, string msg, ConsoleColor color)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            var text = $"[{ts}] [{level}] {msg}";

            lock (_sync)
            {
                WriteConsole(text, color);

                if (WriteToFile && _logFile.Length != 0)
                {
                    try { File.AppendAllText(_logFile, text + Environment.NewLine); }
                    catch { /* ignore IO errors */ }
                }
            }
        }

        internal static void WriteConsole(string text, ConsoleColor color)
        {
            try
            {
                var previous = Console.ForegroundColor;
                try { Console.ForegroundColor = color; Console.WriteLine(text); }
                finally { Console.ForegroundColor = previous; }
            }
            catch { /* A missing/closed console must not kill the background worker. */ }
        }

        // ��������������������������������������������������������������������������������������������������������������������������
        // Graceful shutdown
        // ��������������������������������������������������������������������������������������������������������������������������
        public static void Shutdown()
        {
            _running = false;
            _worker?.Join(200);
        }
    }
}

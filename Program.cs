using System;
using MystieHook.Games;
using MystieHook.Games.Example;
using MystieHook.Games.Wardog;
using MystieHook.Services;
using Raylib_cs;
using static MystieHook.Misc;
using static MystieHook.OverlayUI;
// using static MystieHook.OverlayUI; // ← remove this

namespace MystieHook
{
    internal static class Program
    {
        private enum UiChoice { Advanced, Simple, Game }

        private static UiChoice AskUiChoice(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i].Trim().ToLowerInvariant();
                if (a is "--ui" or "-u")
                {
                    if (i + 1 < args.Length)
                    {
                        var val = args[i + 1].Trim().ToLowerInvariant();
                        if (val.StartsWith("adv")) return UiChoice.Advanced;
                        if (val.StartsWith("simp")) return UiChoice.Simple;
                        if (val.StartsWith("game")) return UiChoice.Game;
                    }
                }
                else if (a is "advanced" or "adv") return UiChoice.Advanced;
                else if (a is "simple" or "simp") return UiChoice.Simple;
                else if (a is "game") return UiChoice.Game;
            }

            // Optional: skip prompt if no console (keeps Advanced as default)
            return UiChoice.Game;
        }

        private static (string title, Action draw) ResolveUi(UiChoice choice)
            => choice switch
            {
                UiChoice.Simple   => ("MystieHook · Simple",   ServiceDemoUI.Draw),
                UiChoice.Advanced => ("MystieHook · Advanced", OverlayUI.Draw),
                UiChoice.Game     => ("MystieHook", () => GameSelector.Draw()),
            };

        private static int Main(string[] args)
        {
            if(args.Contains("--print-read-optimization")){Console.WriteLine(WardogReadOptimization.Enabled ? "enabled" : "disabled" );return 0;}
            if(args.Contains("--print-lite-batching")){Console.WriteLine(WardogReadOptimization.LiteBatching ? "enabled" : "disabled");return 0;}
            // Offline verification exits before graphics, radar, input or DMA workers start.
            if(args.Contains("--verify-package"))
            {
                try { StartupDiagnostics.CheckAssets(); StartupDiagnostics.CheckNativeLibraries(); Console.WriteLine("Package dependencies verified without opening DMA."); return 0; }
                catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            StartupDiagnostics.Initialize();
            AppDomain.CurrentDomain.UnhandledException += (_, e) => StartupDiagnostics.Record("Unhandled exception: " + e.ExceptionObject);
            try
            {
                // Explorer, shortcuts and launchers may start us in another directory.
                System.IO.Directory.SetCurrentDirectory(AppContext.BaseDirectory);
                StartupDiagnostics.Record("Checking application files");
                StartupDiagnostics.CheckAssets();



                if (args.Contains("--diagnose-startup"))
                {
                    StartupDiagnostics.CheckNativeLibraries();
                    StartupDiagnostics.Record("Startup dependency checks passed. DMA hardware was not opened.");
                    return 0;
                }
                Run(args);
                return 0;
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Record("Startup/application failure: " + ex);
                Console.Error.WriteLine("Open Start-Wardog.cmd to retain the error output. Log: " + StartupDiagnostics.LogPath);
                return 1;
            }
        }

        private static void Run(string[] args)
        {
            JobSystem.Start(workers: 3);
            var choice = AskUiChoice(args);
            var (title, drawLoop) = ResolveUi(UiChoice.Game);

            StartupDiagnostics.Record("Creating graphics window");
            using var win = new OverlayWindow(title, 1100, 700);
            OverlayWindowApi.Bind(win);
            GameRegistry.Register(new WardogGame { AutoConnect = !args.Contains("--preview-menu") });


            Image icon = Raylib.LoadImage("Assets/Img/Logo.png");
            Raylib.SetWindowIcon(icon);
            Raylib.UnloadImage(icon);
            Win32IconHelper.SetWindowIcons("Assets/Img/Logo.ico");

            StartupDiagnostics.Record("Interface ready; DMA is initialized only when connecting");
            int previewFrame = 0;
            bool previewMenu = args.Contains("--preview-menu");
            if (previewMenu) win.AfterDraw = () =>
            {
                previewFrame++;
                if (previewFrame % 10 == 0)
                    Raylib.TakeScreenshot(System.IO.Path.Combine(AppContext.BaseDirectory, $"menu-preview-{previewFrame / 10 - 1}.png"));
                if (previewFrame >= 60) GameSelector.PreviewPanic();
            };
            try { win.Run(() =>
            {
                if (previewMenu)
                {
                    // Local UI verification only: never starts the DMA reader.
                    GameSelector.PreviewMenuTab(previewFrame / 10);
                }
                drawLoop();
            }); }
            finally
            {
                try { GameSelector.FlushFeatureColors(); } catch { }
                try { GameRegistry.StopActive(); } catch { }
                LocalRadarService.StopAll();
                try { VmmService.DisposeVmm(); } catch { }
                try { JobSystem.Stop(); } catch { }
            }
        }
    }
}

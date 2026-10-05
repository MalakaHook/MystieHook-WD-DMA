using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using static MystieHook.OverlayWindow;
using MystieHook.Services;
using MystieHook.Games;
using static MystieHook.Misc;
using static MystieHook.StyleEditorUI;

namespace MystieHook;

public static class OverlayUI
{
    // ---------- Display/Window ----------
    private static int _selectedMonitor = 0;
    private static bool _applyBorderless = true;
    private static bool _applyFullscreen = false;
    private static bool _undecoratedApplied = false;
    private static string _appName = "MystieHook";
    static bool _uiUseVsync = false;
    static int  _uiFpsCap   = 144;
    // ---------- UI State ----------
    private static string _procName = "explorer.exe";
    private static string _procFilter = "";
    private static int _procSelectedIndex = -1;

    private static string _modFilter = "";
    private static int _modSelected = -1;

    // Memory tab – local chain builder (kept client-side)
    enum ChainStepKind { StartBase, AddOffset, AddConst, AddScaledIndex, Deref, ReadAs }
    private enum ReturnAs { Ptr, U64, I64, I32, F32, String, Utf16 }
    private struct ChainStep
    {
        public ChainStepKind Kind;
        public string OffsetName;
        public ulong Const;
        public int Index;
        public ulong Stride;
        public bool IndexPlusOne;
        public ReturnAs ReadType;
        public int StringMax;
    }

    private static readonly List<ChainStep> _chain = new();
    private static ulong _activeBase;
    private static string _activeBaseLabel = "Main module";
    private static bool _offsetsLoaded;
    private static List<string> _offsetNames = new();
    private static int _selOffsetIdx = -1;
    private static string _lastEvalSummary = "";
    private static ulong _lastEvalAddress;
    private static ulong _uiAddConst = 0x10;
    private static int _uiIndex = 0;
    private static bool _uiPlusOne = true;
    private static ulong _uiStride = 0x78;

    // ---------- Public Draw ----------
    public static void Draw()
    {
        var winFlags = StyleConfig.MakeWindowFlags();

        DrawHome(winFlags);
        DrawMemory(winFlags);
        DrawAbout(winFlags);
        DrawTopInfoBar();

        StyleEditorUI.Draw();
    }
    private static bool _procAutoRefreshedOnce = false;
    // ---------- Home ----------
    private static void DrawHome(ImGuiWindowFlags winFlags)
    {
        ImGui.PushFont(Fonts.Bold);
        bool open = ImGui.Begin("MystieHook · Home", winFlags);
        ImGui.PopFont();
        if (!open) { ImGui.End(); return; }

        var snap = Snapshots.Current;
        BeginPanel("games_panel", "Games");
        bool openGames = BeginFold("home_games", "Games (Switch between UIs)", defaultOpen: true);
        if (openGames)
        {
            // Build a simple combo of registered games
            var names = GameRegistry.Names?.ToList() ?? new List<string>();
            string[] arr = names.ToArray();
            int cur = Math.Max(0, arr.ToList().IndexOf(GameRegistry.Active?.Name ?? (arr.Length > 0 ? arr[0] : "")));
            string preview = arr.Length > 0 ? arr[cur] : "(no games registered)";
        
            using (UiLayout.PushFieldWidth(240, 380))
            {
                if (ImGui.BeginCombo("Active Game", preview))
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        bool sel = (i == cur);
                        if (ImGui.Selectable(arr[i], sel))
                            GameRegistry.Select(arr[i]);
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
        
            if (ImGui.Button("Stop Active Game")) GameRegistry.StopActive();
            ImGui.SameLine();
            if (ImGui.Button("Start/Restart Active"))
            {
                var g = GameRegistry.Active;
                g?.Stop();
                g?.Start();
            }
        
            EndFold(openGames);
        }
        EndPanel();
        // Process / DMA
        BeginPanel("proc_panel", "Process / DMA");
        open = BeginFold("home_dma_controls", "DMA Controls", defaultOpen: true);
        if (open)
        {
            ImGui.TextWrapped("Initialize VMM (no attach) to load the memory map, then browse processes. You can still attach later.");

            ImGui.Spacing();
            ImGui.PushFont(Fonts.Medium);
            int clicked = UiLayout.ButtonRowAuto("Init VMM (no attach)", "Attach to Process", "Dispose VMM");
            ImGui.PopFont();

            if (clicked == 0)
            {
                VmmService.InitOnly(); // async
            }
            else if (clicked == 1)
            {
                using (UiLayout.PushFieldWidth(60, 220))
                    ImGui.InputText("Process", ref _procName, 256);
                VmmService.Attach(_procName); // async
            }
            else if (clicked == 2)
            {
                VmmService.DisposeVmm(); // implement in service if dispose can stall
            }

            var vmmColor = snap.VmmReady ? new Vector4(0, 0.8f, 0, 1) : new Vector4(1f, 0.3f, 0.2f, 1);
            DrawStatusInline(vmmColor, snap.VmmReady ? "VMM Ready" : "VMM Not Initialized");
            ImGui.SameLine();
            ImGui.TextDisabled(snap.Status ?? "Idle");

            EndFold(open);
        }
        EndPanel();

        // Process list
        BeginPanel("proc_list", "Processes");
        open = BeginFold("home_proc_list", "Process List", defaultOpen: false);
        if (open)
        {
            if (!snap.VmmReady)
            {
                ImGui.TextDisabled("Init VMM first to enumerate processes.");
            }
            else
            {
                using (UiLayout.PushFieldWidth(40, 220))
                    ImGui.InputText("Filter (name substring)", ref _procFilter, 128);

                ImGui.SameLine();
                if (ImGui.Button("Refresh")) VmmService.RefreshProcesses();

                // Auto-refresh ONCE (or use a small cooldown)
                if (!_procAutoRefreshedOnce && (snap.Processes?.Length ?? 0) == 0)
                {
                    _procAutoRefreshedOnce = true;
                    VmmService.RefreshProcesses();
                }
                // --- OR with cooldown (uncomment if you prefer) ---
                // var now = ImGui.GetTime(); // double seconds since start
                // if ((snap.Processes?.Length ?? 0) == 0 && now >= _nextProcRefreshTime)
                // {
                //     _nextProcRefreshTime = now + 1.0; // 1-second cooldown
                //     VmmService.RefreshProcesses();
                // }
            }
            ImGui.BeginChild("proc_list_child", new Vector2(0, 240), ImGuiChildFlags.None);
            var list = (snap.Processes ?? Array.Empty<DmaMemory.ProcEntry>())
                .Where(p => string.IsNullOrWhiteSpace(_procFilter) ||
                            (p.Name?.IndexOf(_procFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();

            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                bool selected = i == _procSelectedIndex;
                if (ImGui.Selectable($"{p.Pid,6}  {p.Name}{(p.IsWow64 ? "  (Wow64)" : "")}", selected))
                    _procSelectedIndex = i;
            }
            if (list.Count == 0) ImGui.TextDisabled("(no processes match filter)");
            ImGui.EndChild();

            if (_procSelectedIndex >= 0 && _procSelectedIndex < list.Count)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Attach to selected process:");
                ImGui.SameLine();
                if (ImGui.Button("Attach"))
                {
                    var sel = list[_procSelectedIndex];
                    VmmService.Attach(sel.Name); // async
                }
            }

            EndFold(open);
        }
        EndPanel();

        // Display / Monitors
        BeginPanel("display_panel", "Display");

        open = BeginFold("home_display", "Monitor & Window", defaultOpen: true);
        if (open)
        {            
            ImGui.Checkbox("Use VSync (syncs to monitor refresh)", ref _uiUseVsync);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            ImGui.InputInt("FPS Cap", ref _uiFpsCap);

            if (ImGui.Button("Apply Frame Pacing"))
            {
                // push values into the window instance (store a reference, or expose a setter)
                OverlayWindowApi.SetFramePacing(_uiUseVsync, _uiFpsCap);
            }            
            int monCount = Raylib.GetMonitorCount();
            if (_selectedMonitor >= monCount) _selectedMonitor = Math.Max(0, monCount - 1);

            var labels = new List<string>(monCount);
            for (int i = 0; i < monCount; i++)
                labels.Add($"Monitor {i}  ({Raylib.GetMonitorWidth(i)}×{Raylib.GetMonitorHeight(i)})  @{Raylib.GetMonitorRefreshRate(i)}Hz");

            using (UiLayout.PushFieldWidth(260, 420))
            {
                string preview = monCount > 0 ? labels[_selectedMonitor] : "(no monitors)";
                if (ImGui.BeginCombo("Monitor", preview))
                {
                    for (int i = 0; i < monCount; i++)
                    {
                        bool sel = (i == _selectedMonitor);
                        if (ImGui.Selectable(labels[i], sel)) _selectedMonitor = i;
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }

            ImGui.Checkbox("Borderless (undecorated)", ref _applyBorderless);
            ImGui.SameLine();
            ImGui.Checkbox("Fullscreen", ref _applyFullscreen);

            int b = UiLayout.ButtonRowAuto("Apply", "Center Window", "Restore Decorations");
            if (b == 0)
            {
                Misc.ApplyMonitorSelection(_selectedMonitor, _applyBorderless, _applyFullscreen);
            }
            else if (b == 1)
            {
                Misc.CenterOnMonitor(_selectedMonitor);
            }
            else if (b == 2)
            {
                Misc.RestoreDecorations();
            }

            ImGui.Separator();
            ImGui.TextDisabled("Tip: Borderless + sizing to monitor gives a clean fullscreen feel without Alt-Tab quirks.");

            EndFold(open);
        }
        EndPanel();

        ImGui.End();
    }

    // ---------- Memory ----------
    private static void DrawMemory(ImGuiWindowFlags winFlags)
    {
        ImGui.PushFont(Fonts.Bold);
        bool memOpen = ImGui.Begin("MystieHook · Memory", winFlags);
        ImGui.PopFont();
        if (!memOpen) { ImGui.End(); return; }

        var snap = Snapshots.Current;
        if (!snap.VmmReady)
        {
            ImGui.TextDisabled("Attach/init VMM to use the memory tools.");
            ImGui.End();
            return;
        }

        // Load offsets.json once
        if (!_offsetsLoaded)
        {
            Offsets.EnsureExistsWithExample();
            _offsetsLoaded = Offsets.Load();
            _offsetNames = Offsets.Names().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            if (_offsetNames.Count > 0) _selOffsetIdx = 0;
        }

        var open = BeginFold("mem_top", "Process Info", defaultOpen: true);
        if (open)
        {            
            ImGui.BulletText($"Status: {snap.Status}");
            if (_activeBase == 0) { _activeBase = DmaMemory.Base; _activeBaseLabel = "Main module"; }
            ImGui.BulletText($"Main Base: 0x{DmaMemory.Base:X}");
            ImGui.BulletText($"Active Base: 0x{_activeBase:X}  ({_activeBaseLabel})");
            EndFold(open);
        }

        ImGui.Separator();

        // Modules (from snapshot)
        open = BeginFold("mem_modules", "Modules (pick Active Base)", defaultOpen: true);
        if (open)
        {            
            using (UiLayout.PushFieldWidth(260, 420))
                ImGui.InputText("Filter (e.g. client.dll)", ref _modFilter, 128);

            ImGui.SameLine();
            if (ImGui.Button("Refresh Modules")) VmmService.RefreshModules(); // async
            if ((snap.Modules?.Length ?? 0) == 0) VmmService.RefreshModules();

            ImGui.BeginChild("mods_child", new Vector2(0, 160), ImGuiChildFlags.None);
            var mods = (snap.Modules ?? Array.Empty<DmaMemory.ModuleInfo>())
                .Where(m =>
                    string.IsNullOrWhiteSpace(_modFilter) ||
                    m.Name.IndexOf(_modFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.FullName.IndexOf(_modFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                bool sel = (i == _modSelected);
                if (ImGui.Selectable($"{m.Name,-28}  Base=0x{m.Base:X}  Size=0x{m.Size:X}", sel))
                    _modSelected = i;
            }
            if (mods.Count == 0) ImGui.TextDisabled("(no modules match filter)");
            ImGui.EndChild();

            if (ImGui.Button("Use Main Module Base"))
            {
                _activeBase = DmaMemory.Base;
                _activeBaseLabel = "Main module";
            }
            ImGui.SameLine();
            if (ImGui.Button("Use Selected Module Base"))
            {
                if (_modSelected >= 0 && _modSelected < mods.Count)
                {
                    var m = mods[_modSelected];
                    _activeBase = m.Base;
                    _activeBaseLabel = m.Name;
                }
            }

            EndFold(open);
        }

        ImGui.Separator();


        open = BeginFold("mem_offsets", "Offsets (offsets.json)", defaultOpen: true);
        if (open)
        {

            ImGui.BeginChild("offsets_child", new Vector2(350, 220), ImGuiChildFlags.None);
            if (!_offsetsLoaded)
            {
                ImGui.TextDisabled("Failed to load offsets.json.");
                if (ImGui.Button("Retry Load")) _offsetsLoaded = Offsets.Load();
            }
            else
            {
                for (int i = 0; i < _offsetNames.Count; i++)
                {
                    string n = _offsetNames[i];
                    bool selected = i == _selOffsetIdx;
                    if (ImGui.Selectable(n, selected)) _selOffsetIdx = i;
                }
            }
            ImGui.EndChild();
            EndFold(open);
        }

        ImGui.Separator();

        open = BeginFold("mem_chain", "Read Chain Builder", defaultOpen: true);
        if (open)
        {

            ImGui.TextDisabled("Build a chain: Base → +Offset/Const/(i*stride) → Deref → ReturnAs");

            if (ImGui.Button("Start: Base")) _chain.Add(new ChainStep { Kind = ChainStepKind.StartBase });

            ImGui.SameLine();
            if (ImGui.Button("+ Offset (selected)"))
            {
                if (_selOffsetIdx >= 0 && _selOffsetIdx < _offsetNames.Count)
                    _chain.Add(new ChainStep { Kind = ChainStepKind.AddOffset, OffsetName = _offsetNames[_selOffsetIdx] });
            }

            ImGui.SameLine();
            if (ImGui.Button("→ Deref to Ptr")) _chain.Add(new ChainStep { Kind = ChainStepKind.Deref });

            ImGui.SameLine();
            using (UiLayout.PushFieldWidth(120, 180))
            {
                if (ImGui.BeginCombo("Return As", "Choose..."))
                {
                    foreach (ReturnAs t in Enum.GetValues(typeof(ReturnAs)))
                        if (ImGui.Selectable(t.ToString()))
                            _chain.Add(new ChainStep { Kind = ChainStepKind.ReadAs, ReadType = t, StringMax = 64 });
                    ImGui.EndCombo();
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled("Index/stride helpers:");

            ImGui.SetNextItemWidth(140);
            InputU64("const", ref _uiAddConst);
            ImGui.SameLine();
            if (ImGui.Button("+ Const"))
                _chain.Add(new ChainStep { Kind = ChainStepKind.AddConst, Const = _uiAddConst });

            ImGui.SameLine();
            ImGui.SetNextItemWidth(60);
            ImGui.InputInt("i", ref _uiIndex);
            ImGui.SameLine();
            ImGui.Checkbox("+1", ref _uiPlusOne);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            InputU64("stride", ref _uiStride);
            ImGui.SameLine();
            if (ImGui.Button($"+ (i{(_uiPlusOne ? "+1" : "")})*stride"))
                _chain.Add(new ChainStep { Kind = ChainStepKind.AddScaledIndex, Index = _uiIndex, IndexPlusOne = _uiPlusOne, Stride = _uiStride });

            if (_chain.Count > 0)
            {
                var last = _chain[^1];
                if (last.Kind == ChainStepKind.ReadAs && (last.ReadType == ReturnAs.String || last.ReadType == ReturnAs.Utf16))
                {
                    int mx = last.StringMax <= 0 ? 64 : last.StringMax;
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(80);
                    if (ImGui.InputInt("max", ref mx))
                    {
                        mx = Math.Clamp(mx, 1, 4096);
                        last.StringMax = mx;
                        _chain[^1] = last;
                    }
                }
            }

            ImGui.Spacing();

            ImGui.BeginChild("chain_steps", new Vector2(0, 180), ImGuiChildFlags.None);
            for (int i = 0; i < _chain.Count; i++)
            {
                var s = _chain[i];
                ImGui.Bullet();
                switch (s.Kind)
                {
                    case ChainStepKind.StartBase:
                        ImGui.TextUnformatted($"Start: Base (0x{_activeBase:X} · {_activeBaseLabel})"); break;
                    case ChainStepKind.AddOffset:
                        ImGui.TextUnformatted($"+ Offset: {s.OffsetName}"); break;
                    case ChainStepKind.AddConst:
                        ImGui.TextUnformatted($"+ Const: 0x{s.Const:X}"); break;
                    case ChainStepKind.AddScaledIndex:
                        ImGui.TextUnformatted($"+ ({(s.IndexPlusOne ? "i+1" : "i")}={(s.IndexPlusOne ? s.Index + 1 : s.Index)}) * 0x{s.Stride:X}"); break;
                    case ChainStepKind.Deref:
                        ImGui.TextUnformatted("→ Deref to Ptr"); break;
                    case ChainStepKind.ReadAs:
                        ImGui.TextUnformatted($"Return As: {s.ReadType}{(s.ReadType == ReturnAs.String || s.ReadType == ReturnAs.Utf16 ? $" (max {s.StringMax})" : "")}"); break;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton($"X##{i}")) { _chain.RemoveAt(i); i--; }
            }
            ImGui.EndChild();

            ImGui.Spacing();
            if (ImGui.Button("Evaluate")) EvaluateChain(out _lastEvalSummary, out _lastEvalAddress);
            ImGui.SameLine();
            if (ImGui.Button("Clear Chain")) { _chain.Clear(); _lastEvalSummary = ""; }

            EndFold(open);
        }

        ImGui.Separator();

        open = BeginFold("mem_results", "Results", defaultOpen: true);
        if (open)
        {            
            ImGui.Text("Result:");
            if (string.IsNullOrEmpty(_lastEvalSummary)) ImGui.TextDisabled("(none)");
            else ImGui.TextWrapped(_lastEvalSummary);
            EndFold(open);
        }

        ImGui.End();
    }

    // ---------- About ----------
    private static void DrawAbout(ImGuiWindowFlags winFlags)
    {
        ImGui.PushFont(Fonts.Bold);
        bool aboutOpen = ImGui.Begin("MystieHook · About", winFlags);
        ImGui.PopFont();
        if (!aboutOpen) { ImGui.End(); return; }


        var open = BeginFold("about_tabs", "About (Tabs)", defaultOpen: true);
        if (open)
        {            
            if (ImGui.BeginTabBar("AboutTabs"))
            {
                ImGui.PushFont(Fonts.Bold);
                bool infoTabOpen = ImGui.BeginTabItem("Info");
                ImGui.PopFont();
                if (infoTabOpen)
                {
                    ImGui.TextWrapped("MystieHook example | ImGui.NET + Raylib + rlImGui_cs.");
                    ImGui.EndTabItem();
                }

                ImGui.PushFont(Fonts.Bold);
                bool creditsTabOpen = ImGui.BeginTabItem("Credits");
                ImGui.PopFont();
                if (creditsTabOpen)
                {
                    ImGui.Text("Thanks to Lone for VmmSharpEx");
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            EndFold(open);
        }

        ImGui.End();
    }

    // ---------- Top Info Bar ----------
    private static void DrawTopInfoBar()
    {
        if (!_undecoratedApplied && _applyBorderless)
        {
            Raylib.SetWindowState(ConfigFlags.UndecoratedWindow);
            _undecoratedApplied = true;
        }

        var vp = ImGui.GetMainViewport();
        const float chipW = 420f, chipH = 34f;
        var posX = vp.WorkPos.X + (vp.WorkSize.X - chipW) * 0.5f;
        var posY = vp.WorkPos.Y + 8f;

        ImGui.SetNextWindowPos(new Vector2(posX, posY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(chipW, chipH), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 8));

        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav;

        ImGui.Begin("TopBarChip", flags);

        // Logo dot
        var dl = ImGui.GetWindowDrawList();
        var pMin = ImGui.GetCursorScreenPos();
        var center = new Vector2(pMin.X + 12, pMin.Y + chipH * 0.5f);
        dl.AddCircleFilled(center, 8f, ImGui.GetColorU32(new Vector4(0.2f, 0.6f, 1f, 1f)));
        ImGui.Dummy(new Vector2(26, chipH - 12));
        ImGui.SameLine();

        // Text
        ImGui.PushFont(Fonts.Bold);
        ImGui.TextUnformatted(_appName);
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextDisabled($"· {ImGui.GetIO().Framerate:0} FPS");

        // Close button aligned right
        const float rightBtnW = 26f;
        ImGui.SameLine();
        float avail = ImGui.GetContentRegionAvail().X;
        if (avail > rightBtnW) ImGui.Dummy(new Vector2(avail - rightBtnW, 1));
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 0.3f, 0.3f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.15f, 0.15f, 1f));
        if (ImGui.Button("X", new Vector2(rightBtnW, chipH - 14)))
        {
            OverlayWindowApi.Quit();
        }
        ImGui.PopStyleColor(3);

        ImGui.End();
        ImGui.PopStyleVar(3);
    }

    // ---------- Memory helpers ----------
    private static unsafe bool InputU64(string label, ref ulong value, string format = "0x%llX")
    {
        fixed (ulong* p = &value)
            return ImGui.InputScalar(label, ImGuiDataType.U64, (nint)p, IntPtr.Zero, IntPtr.Zero, format);
    }

    private static void EvaluateChain(out string desc, out ulong lastAddr)
    {
        // Same as your earlier version; left as-is for brevity.
        // If this ever blocks, wrap it into a Service job too.
        desc = ""; lastAddr = 0;
        if (_chain.Count == 0) { desc = "Empty chain."; return; }

        ulong cur = 0; bool haveStart = false; string log = "";
        for (int i = 0; i < _chain.Count; i++)
        {
            var s = _chain[i];
            switch (s.Kind)
            {
                case ChainStepKind.StartBase:
                    cur = (_activeBase != 0 ? _activeBase : DmaMemory.Base);
                    haveStart = true; log += $"Start = Base (0x{cur:X})\n"; break;

                case ChainStepKind.AddOffset:
                    if (!haveStart) { desc = "Chain must start with Base."; return; }
                    if (!Offsets.TryGet(s.OffsetName, out var ofs)) { desc = $"Unknown offset '{s.OffsetName}'."; return; }
                    cur += ofs; log += $"+ {s.OffsetName} (0x{ofs:X}) = 0x{cur:X}\n"; break;

                case ChainStepKind.AddConst:
                    if (!haveStart) { desc = "Chain must start with Base."; return; }
                    cur += s.Const; log += $"+ 0x{s.Const:X} = 0x{cur:X}\n"; break;

                case ChainStepKind.AddScaledIndex:
                    if (!haveStart) { desc = "Chain must start with Base."; return; }
                    long ii = s.IndexPlusOne ? (long)s.Index + 1 : s.Index;
                    ulong add = (ulong)ii * s.Stride;
                    cur += add; log += $"+ ({(s.IndexPlusOne ? "i+1" : "i")}={ii})*0x{s.Stride:X} = +0x{add:X} -> 0x{cur:X}\n"; break;

                case ChainStepKind.Deref:
                    if (!haveStart) { desc = "Chain must start with Base."; return; }
                    if (!DmaMemory.Read(cur, out ulong ptr)) { desc = $"Deref @ 0x{cur:X} failed."; return; }
                    log += $"*[0x{cur:X}] => 0x{ptr:X}\n"; cur = ptr; break;

                case ChainStepKind.ReadAs:
                    if (!haveStart) { desc = "Chain must start with Base."; return; }
                    lastAddr = cur;
                    switch (s.ReadType)
                    {
                        case ReturnAs.Ptr: desc = log + $"Return Ptr @ 0x{cur:X}"; return;
                        case ReturnAs.U64: if (DmaMemory.Read(cur, out ulong u64)) desc = log + $"u64 @ 0x{cur:X} = {u64} (0x{u64:X})"; else desc = log + $"u64 read @ 0x{cur:X} failed."; return;
                        case ReturnAs.I64: if (DmaMemory.Read(cur, out long i64)) desc = log + $"i64 @ 0x{cur:X} = {i64} (0x{i64:X})"; else desc = log + $"i64 read @ 0x{cur:X} failed."; return;
                        case ReturnAs.I32: if (DmaMemory.Read(cur, out int i32)) desc = log + $"i32 @ 0x{cur:X} = {i32} (0x{i32:X})"; else desc = log + $"i32 read @ 0x{cur:X} failed."; return;
                        case ReturnAs.F32: if (DmaMemory.Read(cur, out float f32)) desc = log + $"f32 @ 0x{cur:X} = {f32}"; else desc = log + $"f32 read @ 0x{cur:X} failed."; return;
                        case ReturnAs.Utf16: { var smax = Math.Clamp(s.StringMax, 1, 4096); var text = DmaMemory.ReadUtf16Z(cur, smax) ?? ""; desc = log + $"utf16 @ 0x{cur:X} = \"{text}\""; return; }
                        case ReturnAs.String: { var smax = Math.Clamp(s.StringMax <= 0 ? 64 : s.StringMax, 1, 4096); var text = DmaMemory.ReadAsciiZ(cur, smax) ?? ""; desc = log + $"string @ 0x{cur:X} = \"{text}\""; return; }
                    }
                    break;
            }
        }
        lastAddr = cur; desc = log + $"Final Addr = 0x{cur:X} (add a 'Return As' step to read)";
    }

    // ---------- Small UI utils ----------
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
}

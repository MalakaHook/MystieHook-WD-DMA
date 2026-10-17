// MystieHook/Games/GameSelector.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImGuiNET;
using MystieHook.Gui;
using MystieHook.Input;
using MystieHook.Services;
using Raylib_cs;
using static MystieHook.Misc;
using static MystieHook.OverlayWindow;
using static MystieHook.StyleEditorUI;

namespace MystieHook.Games
{

    // ---------------------------------------------------------------------
    // Keybind config model
    // ---------------------------------------------------------------------
    // ---------------------------------------------------------------------
    // GameSelector (with separate Keybinds window + Hide-All-Menus)
    // ---------------------------------------------------------------------
    public static partial class GameSelector
    {
        private static bool _customMakcuFirmware;
        // Global flags
        public static bool VmmInnit { get; private set; }
        public static bool AttachedToProccess { get; private set; }

        private static bool _loadedLastGame = false;
        private static readonly string _lastGamePath =
            System.IO.Path.Combine(AppContext.BaseDirectory, "last_game.txt");
        private static int _selectedMonitor = 0;
        private static bool _applyBorderless = true;
        private static bool _applyFullscreen = false;
        private static bool _undecoratedApplied = false;
        static bool _uiUseVsync = false;
        static int _uiFpsCap = 120;

        private static readonly string[] _globalActions =
            Enum.GetNames(typeof(KeybindAction));

        // Process/Module UI state
        private static int _procSelectedIndex = -1;
        private static string _procFilter = string.Empty;
        private static string _modFilter = string.Empty;

        // transitions

        // Windows visibility

        // Global UI hide (menus off, overlays/ESP still run)
        private static bool _allMenusHidden = false;

        // Top bar

        // Input
        private static int _selectedInputIndex = -1;
        private static List<MystieHook.Input.Device.SerialDeviceInfo> _inputDevices = new();
        private static bool _inputInitialized = false;

        // Keybinds state
        private static KeybindProfile _activeProfile = new();
        private static bool _wantsRebind = false;   // editor capture flag
        private static int _rebindVk = 0;
        private static bool _rebindCtrl, _rebindShift, _rebindAlt;
        private static int _editingIndex = -1;      // which bind we edit (-1 = adding)
        private static string _newBindName = "New Bind";
        private static string _editActionName = nameof(KeybindAction.ShowMenu);
        private static KeybindMode _newBindMode = KeybindMode.OnPress;

        // cache hotkey registrations to avoid duplicates
        private static bool _hotkeysRegistered = false;

        public static int SelectedMonitor => _selectedMonitor;

        public static void Draw()
        {
            GameHost.Tick();
            Assets.Load();
            InitializeMenu();
            UpdateInputDeviceDiscovery();
            PollDiagnosticsExport();
            // Local recovery shortcut also works before DMA input is connected.

            // Sync snapshot
            var s = Snapshots.Current;
            VmmInnit = s.VmmReady;
            AttachedToProccess = s.VmmReady && s.Pid > 0 && s.MainBase != 0;

            if (GameRegistry.Active is Wardog.WardogGame wardog) wardog.DrawOverlay();
            if (GameRegistry.Active is Example.ExampleGame) Example.ExampleGame.DrawEntitiesOverlay();
            if (!_allMenusHidden) DrawUnifiedMenu();
            ProcessColorAutosave();
            if (_showFps) DrawFps();
            ProcessLocalKeybinds();
            ExecuteKeybindsFrame();
            DrawConfigurationNotice();
        }

        private static void DrawSettingsContent()
        {
            DrawConnectionSummary();
            ImGui.Separator();
            MenuTools.Checkbox("Show FPS", ref _showFps);
            ImGui.SameLine();
            if (ImGui.Button("Exit cheat")) OverlayWindowApi.Quit();
            ImGui.Separator();
            if (GameRegistry.Active is Wardog.WardogGame wardog)
            {
                wardog.DrawSettings();
                wardog.DrawPerformanceHistory();
                DrawDiagnosticsExport(wardog);
            }
            ImGui.Spacing();
            ImGui.SeparatorText("Display & Frame Pacing");
            DrawMonitorSettings();

            ImGui.Spacing();
        }
        // ---------------- Keybinds Window ----------------

        private static volatile bool _bindEditorOpen;
        private static bool _bindKeyWarning;

        private static void DrawKeybindsWindow()
        {
            if (_bindEditorOpen)
            {
                DrawBindEditor();
                return;
            }


            ImGui.TextUnformatted($"Configuration: {_currentConfig}");
            ImGui.TextDisabled("Save the current configuration to keep your keybind changes.");
            ImGui.TextWrapped(_configurationStatus);
            ImGui.Separator();
            ImGui.TextDisabled("Binds:");

            // --- compute column widths from content (auto-fit, no wasted space) ---
            var style = ImGui.GetStyle();
            float cellPadX   = style.CellPadding.X;
            float framePadX  = style.FramePadding.X;
            float itemSpaceX = style.ItemSpacing.X;

            float TextW(string s) => ImGui.CalcTextSize(s ?? "").X;
            float BtnW(string s)  => TextW(s) + framePadX * 2f;
            string ModsStr(KeybindEntry b) =>
                $"{(b.Ctrl ? "Ctrl " : "")}{(b.Shift ? "Shift " : "")}{(b.Alt ? "Alt" : "")}".Trim();

            float nameW   = TextW("Name");
            float keyW    = TextW("Key");
            float modsW   = TextW("Mods");
            float modeW   = TextW("Mode");
            float actionW = TextW("Action");
            float editW   = TextW("Edit");

            foreach (var b in _activeProfile.Binds)
            {
                nameW   = MathF.Max(nameW,   TextW(b.Name));
                keyW    = MathF.Max(keyW,    TextW(VkNames.Name(b.Vk) ?? $"VK {b.Vk:X2}"));
                modsW   = MathF.Max(modsW,   TextW(ModsStr(b)));
                modeW   = MathF.Max(modeW,   TextW(b.Mode.ToString()));
                actionW = MathF.Max(actionW, TextW(b.Action));
            }

            float editBtnW = BtnW("Edit") + itemSpaceX + BtnW("Delete");
            editW = MathF.Max(editW, editBtnW);

            float pad(float w) => w + cellPadX * 2f + 4f;
            keyW    = pad(keyW);
            modsW   = pad(modsW);
            modeW   = pad(modeW);
            actionW = pad(actionW);
            editW   = pad(editW);

            var tflags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                       | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollX;

            if (ImGui.BeginTable("binds", 6, tflags, new Vector2(0, Math.Min(320, (_activeProfile.Binds.Count + 2) * ImGui.GetTextLineHeightWithSpacing() + 24))))
            {
                ImGui.TableSetupColumn("Name",   ImGuiTableColumnFlags.WidthStretch, 1.0f);
                ImGui.TableSetupColumn("Key",    ImGuiTableColumnFlags.WidthFixed,   keyW);
                ImGui.TableSetupColumn("Mods",   ImGuiTableColumnFlags.WidthFixed,   modsW);
                ImGui.TableSetupColumn("Mode",   ImGuiTableColumnFlags.WidthFixed,   modeW);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed,   actionW);
                ImGui.TableSetupColumn("Edit",   ImGuiTableColumnFlags.WidthFixed,   editW);
                ImGui.TableHeadersRow();

                for (int i = 0; i < _activeProfile.Binds.Count; i++)
                {
                    var b = _activeProfile.Binds[i];
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(b.Name);
                    ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(VkNames.Name(b.Vk) ?? $"VK {b.Vk:X2}");
                    ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(ModsStr(b));
                    ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(b.Mode.ToString());
                    ImGui.TableSetColumnIndex(4); ImGui.TextUnformatted(b.Action == "ShowMenu" ? "Show menu" : b.Action);

                    ImGui.TableSetColumnIndex(5);
                    if (ImGui.SmallButton($"Edit##{i}")) BeginEditBind(i);
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Delete##{i}"))
                    {
                        _activeProfile.Binds.RemoveAt(i);
                        PersistAndReregister();
                        break;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button("Add Keybind"))
            {
                BeginEditBind(-1);
            }




        }

        private static void BeginEditBind(int index)
        {
            _editingIndex = index;
            if (index >= 0 && index < _activeProfile.Binds.Count)
            {
                var b = _activeProfile.Binds[index];
                _newBindName = b.Name;
                _rebindVk = b.Vk;
                _rebindCtrl = b.Ctrl;
                _rebindShift = b.Shift;
                _rebindAlt = b.Alt;
                _editActionName = b.Action;
                _newBindMode = b.Mode;
            }
            else
            {
                _newBindName = "New Bind";
                _rebindVk = 0;
                _rebindCtrl = _rebindShift = _rebindAlt = false;
                _editActionName = nameof(KeybindAction.ShowMenu);
                _newBindMode = KeybindMode.OnPress;
            }
            _wantsRebind = false;
            _bindKeyWarning = false;
            _bindEditorOpen = true;
        }

        private static void DrawBindEditor()
        {
                ImGui.PushID("InlineBindEditor");
                ImGui.SeparatorText(_editingIndex < 0 ? "Add keybind" : "Edit keybind");
                ImGui.TextDisabled("Save applies this binding. Save in the Config tab to keep it for next launch.");
                ImGui.InputText("Name", ref _newBindName, 128);

                if (!_wantsRebind)
                {
                    if (ImGui.Button("Click to bind"))
                    {
                        _wantsRebind = true;
                        _rebindVk = 0;
                    }
                }
                else
                {
                    ImGui.TextDisabled("Press any key or mouse… (Esc to cancel)");
                    CaptureAnyKey();
                }

                ImGui.SameLine();
                ImGui.Text($"Current: {( _rebindVk==0 ? "<none>" : VkNames.Name(_rebindVk) ?? $"VK {_rebindVk:X2}")}");

                ImGui.Separator();

                ImGui.Checkbox("Ctrl", ref _rebindCtrl); ImGui.SameLine();
                ImGui.Checkbox("Shift", ref _rebindShift); ImGui.SameLine();
                ImGui.Checkbox("Alt", ref _rebindAlt);

                if (ImGui.BeginCombo("Mode", _newBindMode.ToString()))
                {
                    foreach (KeybindMode m in Enum.GetValues(typeof(KeybindMode)))
                    {
                        bool sel = m == _newBindMode;
                        if (ImGui.Selectable(m.ToString(), sel)) _newBindMode = m;
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                // Build the composite action list: Global + Category-specific
                var actions = new List<string>(_globalActions);
                actions.AddRange(MystieHook.Input.Keybinds.GetActions(_activeProfile.Category));

                int curIdx = Math.Max(0, actions.FindIndex(a => string.Equals(a, _editActionName, StringComparison.Ordinal)));
                string preview = (curIdx >= 0 && curIdx < actions.Count) ? actions[curIdx] : actions[0];

                if (ImGui.BeginCombo("Action", preview == "ShowMenu" ? "Show menu" : preview == "Panic" ? "Panic key (exit)" : preview))
                {
                    for (int i = 0; i < actions.Count; i++)
                    {
                        bool sel = (i == curIdx);
                        if (ImGui.Selectable(actions[i] == "ShowMenu" ? "Show menu" : actions[i] == "Panic" ? "Panic key (exit)" : actions[i], sel))
                        {
                            _editActionName = actions[i];
                            curIdx = i;
                        }
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                ImGui.Separator();
                if (ImGui.Button("Save"))
                {
                    if (_rebindVk != 0)
                    {
                        var entry = new KeybindEntry
                        {
                            Name  = _editActionName == "ShowMenu" ? "Show menu" : _newBindName,
                            Vk    = _rebindVk,
                            Ctrl  = _rebindCtrl,
                            Shift = _rebindShift,
                            Alt   = _rebindAlt,
                            Mode  = _newBindMode,
                            Action = _editActionName      // store string
                        };

                        if (_editingIndex >= 0 && _editingIndex < _activeProfile.Binds.Count)
                            _activeProfile.Binds[_editingIndex] = entry;
                        else
                            _activeProfile.Binds.Add(entry);

                        PersistAndReregister();
                        CloseBindEditor();
                    }
                    else
                    {
                        _bindKeyWarning = true;
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    CloseBindEditor();
                }
                if (_bindKeyWarning) ImGui.TextWrapped("Please choose a key or mouse button before saving.");
                ImGui.PopID();
        }

        private static void CloseBindEditor()
        {
            _bindEditorOpen = false;
            _wantsRebind = false;
            _bindKeyWarning = false;
        }

        private static void CaptureAnyKey()
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _wantsRebind = false;
                return;
            }

            bool ctrl = MystieHook.Input.InputManager.IsKeyDown(VK.CONTROL);
            bool shift = MystieHook.Input.InputManager.IsKeyDown(VK.SHIFT);
            bool alt   = MystieHook.Input.InputManager.IsKeyDown(VK.MENU);

            for (int vk = 1; vk < 255; vk++)
            {
                if (MystieHook.Input.InputManager.IsKeyPressed(vk) || (LocalKey(vk) != KeyboardKey.Null && Raylib.IsKeyPressed(LocalKey(vk))))
                {
                    _rebindVk = vk;
                    _rebindCtrl = ctrl || LocalCtrl;
                    _rebindShift = shift || LocalShift;
                    _rebindAlt = alt || LocalAlt;
                    _wantsRebind = false;
                    break;
                }
            }
        }

        private static void PersistAndReregister()
        {
            _hotkeysRegistered = false;
        }
        // Execute binds each frame; use InputManager registrations when ready, else poll
        private static void ExecuteKeybindsFrame()
        {
            if (MystieHook.Input.InputManager.IsReady)
            {
                if (!_hotkeysRegistered)
                {
                    MystieHook.Input.InputManager.ClearAllHotkeys();
                    foreach (var b in _activeProfile.Binds)
                    {
                        var mode = b.Mode switch
                        {
                            KeybindMode.OnPress    => MystieHook.Input.InputManager.HotkeyMode.OnPress,
                            KeybindMode.OnRelease  => MystieHook.Input.InputManager.HotkeyMode.OnRelease,
                            KeybindMode.WhileDown  => MystieHook.Input.InputManager.HotkeyMode.WhileDown,
                            _ => MystieHook.Input.InputManager.HotkeyMode.OnPress
                        };

                        MystieHook.Input.InputManager.RegisterHotkey(b.Vk, mode, () =>
                        {
                            bool ctrl = MystieHook.Input.InputManager.IsKeyDown(VK.CONTROL);
                            bool shift = MystieHook.Input.InputManager.IsKeyDown(VK.SHIFT);
                            bool alt = MystieHook.Input.InputManager.IsKeyDown(VK.MENU);
                            if (!b.MatchesModifiers(ctrl, shift, alt)) return;
                            RunActionString(b.Action);
                        });
                    }
                    _hotkeysRegistered = true;
                }
            }
            else
            {
                foreach (var b in _activeProfile.Binds)
                {
                    bool press = MystieHook.Input.InputManager.IsKeyPressed(b.Vk);
                    bool down  = MystieHook.Input.InputManager.IsKeyDown(b.Vk);
                    bool fire  =
                        (b.Mode == KeybindMode.OnPress   && press) ||
                        (b.Mode == KeybindMode.WhileDown && down);
                    if (!fire) continue;

                    if (b.Ctrl || b.Shift || b.Alt) continue; // can't verify mods here
                    RunActionString(b.Action);
                }
            }
        }

        // New: string-based dispatcher (global enum first, else game/category)
        private static void RunActionString(string actionName)
        {
            if (_bindEditorOpen || string.IsNullOrWhiteSpace(actionName)) return;

            if (Enum.TryParse<KeybindAction>(actionName, out var ga))
            {
                RunAction(ga);
                return;
            }

            // Game/category scoped:
            if (!Keybinds.Dispatch(_activeProfile.Category, actionName))
            {
                // Unknown action; ignore (or log if you want)
                // Console.WriteLine($"[Keybind] Unknown action: {actionName} in category '{_activeProfile.Category}'");
            }
        }

        private static void RunAction(KeybindAction a)
        {
            switch (a)
            {



                case KeybindAction.Panic: OverlayWindowApi.Quit(); break;
                case KeybindAction.ShowMenu:
                    _allMenusHidden = !_allMenusHidden;
                    UiVisibility.MenusHidden = _allMenusHidden; // NEW: tell everyone
                    break;
            }
        }

        private static void TryAttachCurrentlySelectedProcess()
        {
            var s = Snapshots.Current;
            var procs = s.Processes ?? Array.Empty<DmaMemory.ProcEntry>();
            var filtered = FilterProcesses(procs, _procFilter).ToArray();
            if (_procSelectedIndex >= 0 && _procSelectedIndex < filtered.Length)
                VmmService.Attach(filtered[_procSelectedIndex].Name);
        }

        // ---------------- Existing UI helpers ----------------

        private static IEnumerable<DmaMemory.ProcEntry> FilterProcesses(IEnumerable<DmaMemory.ProcEntry> list, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return list;
            filter = filter.Trim();
            bool allDigits = filter.All(char.IsDigit);
            return list.Where(p =>
                (!allDigits && p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                (allDigits && p.Pid.ToString().Contains(filter, StringComparison.Ordinal)));
        }

        private static IEnumerable<DmaMemory.ModuleInfo> FilterModules(IEnumerable<DmaMemory.ModuleInfo> list, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return list;
            filter = filter.Trim();
            return list.Where(m =>
                m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                $"0x{m.Base:X}".Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        private static void LoadLastGameOnce(string[] names)
        {
            if (_loadedLastGame) return;
            _loadedLastGame = true;
            try
            {
                if (File.Exists(_lastGamePath))
                {
                    var wanted = (File.ReadAllText(_lastGamePath) ?? "").Trim();
                    if (!string.IsNullOrEmpty(wanted) &&
                        names.Any(n => string.Equals(n, wanted, StringComparison.Ordinal)))
                    {
                        GameRegistry.Select(wanted);
                    }
                }
            }
            catch { /* ignore */ }
        }

        private static void SaveLastGame(string name)
        {
            try { File.WriteAllText(_lastGamePath, name ?? ""); } catch { /* ignore */ }
        }

        public static void DrawCombo(string label = "Game")
        {
            var names = GameRegistry.Names?.ToArray() ?? Array.Empty<string>();
            if (names.Length == 0)
            {
                ImGui.TextDisabled("No games registered");
                return;
            }

            if (GameRegistry.Active is null)
                LoadLastGameOnce(names);

            if (GameRegistry.Active is null)
                GameRegistry.Select(names[0]);

            var activeName = GameRegistry.Active?.Name ?? names[0];
            int cur = Array.IndexOf(names, activeName);
            if (cur < 0) cur = 0;

            ImGui.SetNextItemWidth(250);
            if (ImGui.BeginCombo(label, names[cur]))
            {
                for (int i = 0; i < names.Length; i++)
                {
                    bool selected = (i == cur);
                    if (ImGui.Selectable(names[i], selected) && i != cur)
                    {
                        GameRegistry.Select(names[i]);

                    }
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        public static void DrawMonitorSettings()
        {
            MenuTools.Checkbox("Use VSync (syncs to monitor refresh)", ref _uiUseVsync);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            MenuTools.InputInt("FPS Cap", ref _uiFpsCap);
            _uiFpsCap = OverlayWindow.ClampFpsCap(_uiFpsCap);

            if (ImGui.Button("Apply Frame Pacing"))
            {
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
            ImGui.Spacing();
            if (ImGui.Button("Apply monitor"))
            {
                Misc.ApplyMonitorSelection(_selectedMonitor, _applyBorderless, _applyFullscreen);
                ScreenService.UpdateFromMonitor(_selectedMonitor);
            }

            ImGui.Separator();
            ImGui.TextDisabled("Rendering fills the monitor and adapts to display changes. Press Windows to access the taskbar.");
        }

        private static bool _inputWasReady;
        private static System.Threading.Tasks.Task<List<Input.Device.SerialDeviceInfo>> _deviceDiscovery;
        private static void RefreshInputDevices()
        {
            if (_deviceDiscovery != null) return;
            _deviceDiscovery = System.Threading.Tasks.Task.Run(() => Input.Device.EnumerateSerialDevices());
        }
        private static void UpdateInputDeviceDiscovery()
        {
            bool ready = Input.InputManager.IsReady;
            if (ready && !_inputWasReady) RefreshInputDevices();
            _inputWasReady = ready;
            if (_deviceDiscovery == null || !_deviceDiscovery.IsCompleted) return;
            var completed = _deviceDiscovery;
            _deviceDiscovery = null;
            try
            {
                // Publish both the list and selection on the UI thread.
                _inputDevices = completed.GetAwaiter().GetResult();
                _selectedInputIndex = _inputDevices.FindIndex(d => d.Port == _preferredInputPort);
                if (_selectedInputIndex < 0 && string.IsNullOrEmpty(_preferredInputPort) && _inputDevices.Count > 0)
                    _selectedInputIndex = 0;
            }
            catch (Exception ex) { Console.WriteLine("[-] Device discovery failed: " + ex.Message); }
        }

        public static void DrawInputManager()
        {
            ImGui.SeparatorText("Input Manager & Makcu");

            ImGui.BeginDisabled(_deviceDiscovery != null);
            if (ImGui.Button("Refresh Devices")) RefreshInputDevices();
            ImGui.EndDisabled();

            ImGui.Separator();
            ImGui.Text("Serial Devices:");

            string preview = (_selectedInputIndex >= 0 && _selectedInputIndex < _inputDevices.Count)
                ? _inputDevices[_selectedInputIndex].ToString()
                : (string.IsNullOrEmpty(_preferredInputPort) ? "(none)" : _preferredInputPort + " (refresh devices)");
            ImGui.SetNextItemWidth(400);
            if (ImGui.BeginCombo("##InputDevices", preview))
            {
                for (int i = 0; i < _inputDevices.Count; i++)
                {
                    bool sel = (i == _selectedInputIndex);
                    if (ImGui.Selectable(_inputDevices[i].ToString(), sel))
                    {
                        _selectedInputIndex = i;
                        _preferredInputPort = _inputDevices[i].Port;
                    }
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Separator();
            ImGui.TextDisabled("Makcu Device");

            ImGui.Checkbox("Custom MAKCU firmware (skip version check)",ref _customMakcuFirmware);
            if(_customMakcuFirmware)ImGui.TextWrapped("Use only with your selected MAKCU-compatible device. Standard serial commands and the high-speed connection are still required.");
            if (ImGui.Button("Connect Makcu"))
            {
                if (_selectedInputIndex >= 0 && _selectedInputIndex < _inputDevices.Count)
                {
                    var dev = _inputDevices[_selectedInputIndex];
                    bool customFirmware=_customMakcuFirmware;
                    JobSystem.Schedule(() =>
                    {
                        if (Input.Device.MakcuConnect(dev.Port,customFirmware))
                            Console.WriteLine($"[+] Makcu connected on {dev.Port}");
                        else
                            Console.WriteLine("[-] Makcu connect failed");
                    });
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Disconnect Makcu"))
            {
                JobSystem.Schedule(() =>
                {
                    Input.Device.Disconnect();
                    Console.WriteLine("[*] Makcu disconnected");
                });
            }

            ImGui.SameLine();
            ImGui.BeginDisabled(!Input.Device.connected);
            if (ImGui.Button("Test mouse"))
                JobSystem.Schedule(() =>
                {
                    try
                    {
                        if (!Input.Device.connected) return;
                        Input.Device.move(30, 0);
                        Console.WriteLine("[*] Makcu test: sent movement 30 units right.");
                    }
                    catch (Exception ex) { Console.WriteLine("[-] Makcu movement test failed: " + ex.Message); }
                });
            ImGui.EndDisabled();
            ImGui.TextDisabled("Test mouse sends a small movement to the right through Makcu.");

            ImGui.Separator();
            ImGui.TextDisabled("Input Manager");

            ImGui.TextWrapped(Input.InputManager.IsReady
                ? "InputManager connected automatically."
                : Input.InputManager.IsInitializing
                    ? "Connecting InputManager automatically..."
                    : "InputManager connects automatically after Wardog attaches.");
            ImGui.TextDisabled("Use Reconnect to Wardog above to restart the connection.");

            ImGui.Separator();
            DrawStatusLight("Makcu Connected", Input.Device.connected);
            if(!Input.Device.connected&&!string.IsNullOrWhiteSpace(Input.Device.LastConnectionError))
            {
                MystieHook.Gui.SafeText.Wrapped(Input.Device.LastConnectionError);
                if(ImGui.Button("Copy Makcu connection error"))ImGui.SetClipboardText(Input.Device.LastConnectionError);
            }
            string firmware=Input.Device.GetVersion();
            MystieHook.Gui.SafeText.Wrapped(string.IsNullOrWhiteSpace(firmware)
                ? "Makcu firmware: unavailable. Connect Makcu to read its version."
                : $"Makcu firmware: {firmware}");
            if(!string.IsNullOrWhiteSpace(firmware)&&ImGui.Button("Copy firmware version"))
                ImGui.SetClipboardText(firmware);
            DrawStatusLight("InputManager Ready", Input.InputManager.IsReady);


        }

        private static void DrawStatusLight(string label, bool state)
        {
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            float y = p.Y + ImGui.GetTextLineHeight() * 0.5f;
            Vector4 col = state
                ? new Vector4(0, 0.8f, 0, 1)
                : new Vector4(0.9f, 0.3f, 0.1f, 1);
            dl.AddCircleFilled(new Vector2(p.X + 6, y), 5, ImGui.ColorConvertFloat4ToU32(col));
            ImGui.Dummy(new Vector2(16, ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            ImGui.TextDisabled(label);
        }
    }
}

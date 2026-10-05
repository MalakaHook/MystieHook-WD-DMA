using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

#nullable enable

namespace MystieHook.Input
{
    [Flags]
    public enum VmmFlags : uint
    {
        NONE = 0, NOCACHE = 1, ZEROPAD_ON_FAIL = 2, FORCECACHE_READ = 8,
        NOPAGING = 0x10, NOPAGING_IO = 0x20, NOCACHEPUT = 0x100, CACHE_RECENT_ONLY = 0x200,
        NO_PREDICTIVE_READ = 0x400, FORCECACHE_READ_DISABLE = 0x800,
        SCATTER_PREPAREEX_NOMEMZERO = 0x1000, NOMEMCALLBACK = 0x2000, SCATTER_FORCE_PAGEREAD = 0x4000
    }

    /// <summary>Minimal adapter you implement to reach VmmSharpEx.</summary>
    public interface IVmmEx
    {
        const uint PID_PROCESS_WITH_KERNELMEMORY = 2147483648u;
        // Registry
        string RegReadString(string path);
        uint RegReadDword(string path);

        // Processes
        uint GetPidByName(string name);                  // single (e.g., winlogon.exe)
        uint[] GetPidsByName(string name);                 // many (e.g., csrss.exe)

        // Modules (pid context)
        bool GetModuleInfo(uint pid, string moduleName, out ulong baseAddress, out ulong imageSize);

        // Memory
        unsafe bool MemRead(uint pid, ulong va, nint pb, uint cb, out uint cbRead, VmmFlags flags);

        // Scanning helpers
        ulong FindSignature(uint pid, ulong start, ulong end, string idaStylePattern); // returns 0 if not found

        // EAT / PDB (optional �? used for Win10 path)
        ulong GetExportVA(uint pid, string moduleName, string function);               // 0 if not found
        bool PdbSymbolAddress(uint pid, string moduleName, string symbol, out ulong va);
    }

    public static class InputManager
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint KM(uint pid) => pid | IVmmEx.PID_PROCESS_WITH_KERNELMEMORY;
        // ===== public API =====
        public static bool IsReady => _initialized && !_safeMode;
        public static int PollIntervalMs { get => _pollMs; set => _pollMs = Math.Max(10, value); }
        // === async init state ===
        private static volatile int _initStarted;        // 0 = not started, 1 = started
        private static Task? _initTask;
        public static bool IsInitializing => _initTask is { IsCompleted: false };
        public static Task BeginInitializeWithRetries(IVmmEx vmm, TimeSpan retryDelay, CancellationToken ct = default, Action<bool,string>? onComplete = null)
        {
            if (IsReady) return Task.CompletedTask;
            if (Interlocked.Exchange(ref _initStarted, 1) == 1)
                return _initTask ?? Task.CompletedTask;

            _initTask = Task.Run(async () =>
            {
                string? lastErr = null;
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            Initialize(vmm);
                            if (IsReady) break;
                        }
                        catch (Exception ex) { lastErr = ex.Message; }
                        _safeMode = false; _initialized = false; _vmm = vmm;
                        await Task.Delay(retryDelay, ct).ConfigureAwait(false);
                    }
                    if (!ct.IsCancellationRequested) onComplete?.Invoke(IsReady, lastErr ?? "");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                finally { Interlocked.Exchange(ref _initStarted, 0); }
            });

            return _initTask;
        }
     
        public static void Initialize(IVmmEx vmm)
        {
            if (_initialized) return;
            _vmm = vmm ?? throw new ArgumentNullException(nameof(vmm));

            Log("[Input] Initialize()�?");

            // Read OS build
            try
            {
                var buildSz = _vmm.RegReadString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\CurrentBuild");
                _build = int.TryParse(buildSz, out var b) ? b : 0;
                _ubr = _vmm.RegReadDword(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\UBR");
                Log($"[Input] Windows build={_build} UBR={_ubr}");
            }
            catch (Exception e)
            {
                Log($"[Input] Registry read failed: {e.Message} �? Safe Mode");
                _safeMode = true;
                return;
            }

            // Find winlogon pid|km flag and resolve gaf
            _pidWinlogon = _vmm.GetPidByName("winlogon.exe");
            if (_pidWinlogon == 0) { Log("[Input] winlogon.exe not found �? Safe Mode"); _safeMode = true; return; }

            // If your VMM needs the “with kernel memory�? flag, OR it into pid here.
            // Keep as-is if your adapter already does it internally:
            _pidWinlogonKM = _pidWinlogon | IVmmEx.PID_PROCESS_WITH_KERNELMEMORY;

            bool ok = (_build > 22000 ? ResolveNewWindows() : ResolveOldWindows())
                      || ResolveOldWindows();
            if (!ok) { _safeMode = true; return; }
            // Prime a first read to verify access
            unsafe
            {
                fixed (byte* p = _curr)
                {
                    if (!_vmm.MemRead(_pidWinlogonKM, _gaf, (nint)p, 64, out var read,
                        VmmFlags.NOCACHE | VmmFlags.NO_PREDICTIVE_READ | VmmFlags.NOCACHEPUT) || read != 64)
                    {
                        Log("[Input] Initial MemRead failed �? Safe Mode");
                        _safeMode = true;
                        return;
                    }
                }
            }

            _initialized = true;
            Log($"[Input] Ready. gaf=0x{_gaf:X}");

            // Start worker
            _run = true;
            _thread = new Thread(Worker) { IsBackground = true, Name = "InputManager" };
            _thread.Start();
        }

        public static void Shutdown()
        {
            _run = false;
            try { _thread?.Join(); } catch { }
            _thread = null;
            _initialized = false;
            Log("[Input] Stopped.");
        }
        private static bool IsMouseVk(int vk, out int idx /* 1..5 */)
        {
            idx = vk switch
            {
                0x01 => 1, // LBUTTON
                0x02 => 2, // RBUTTON
                0x04 => 3, // MBUTTON
                0x05 => 4, // XBUTTON1
                0x06 => 5, // XBUTTON2
                _    => 0
            };
            return idx != 0;
        }

        // External mouse “previous�? frame (for edge detection when we’re not initialized)
        private static readonly bool[] _extMousePrev = new bool[6]; // 1..5 used

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ExternalMouseDown(int idx) // idx: 1..5
        {
            // If you ever want to allow a different provider, replace this with a delegate.
            return Device.connected && Device.button_pressed((MakcuMouseButton)idx);
        }
        public static bool IsKeyDown(int vk)
        {
            // Mouse: always allow external (Makcu) to contribute.
            if (IsMouseVk(vk, out int midx))
            {
                bool ext = ExternalMouseDown(midx);

                // If not initialized (or in safe mode), rely on external only.
                if (!_initialized || _safeMode) return ext;

                // Otherwise OR with gaf bitmap.
                return ext || IsDown(_curr, vk);
            }

            // Non-mouse: original behavior
            return _initialized && !_safeMode && IsDown(_curr, vk);
        }

        public static bool IsKeyPressed(int vk)
        {
            // Mouse: compute a local edge from external provider so it works even when
            // InputManager isn’t fully initialized.
            if (IsMouseVk(vk, out int midx))
            {
                bool now = ExternalMouseDown(midx);
                bool was = _extMousePrev[midx];
                _extMousePrev[midx] = now;

                bool extEdge = now && !was;

                // If not initialized (or in safe mode), use the external edge only.
                if (!_initialized || _safeMode) return extEdge;

                // Otherwise OR with gaf edge (keyboard map) so both sources can trigger.
                bool gafEdge = !IsDown(_prev, vk) && IsDown(_curr, vk);
                return extEdge || gafEdge;
            }

            // Non-mouse: original behavior
            return _initialized && !_safeMode && !IsDown(_prev, vk) && IsDown(_curr, vk);
        }

        /// <summary>Double-tap toggle helper like your original.</summary>
        public static bool IsKeyHeldToggle(int vk)
        {
            if (!IsReady) return false;
            if (!IsKeyPressed(vk))
                return _held.TryGetValue(vk, out var h) && h;

            var now = DateTime.UtcNow;
            lock (_gate)
            {
                if (_lastTap.TryGetValue(vk, out var last) &&
                    (now - last).TotalMilliseconds < DoubleTapMs)
                {
                    _held[vk] = !_held.GetValueOrDefault(vk, false);
                    _lastTap.Remove(vk);
                }
                else _lastTap[vk] = now;
            }
            return _held.TryGetValue(vk, out var v) && v;
        }

        // Hotkeys (simple)
        public enum HotkeyMode { OnPress, OnRelease, WhileDown }
        public static void RegisterHotkey(int vk, HotkeyMode mode, Action action)
        {
            if (action == null) return;
            lock (_hk)
            {
                if (!_hotkeys.TryGetValue(vk, out var list))
                    _hotkeys[vk] = list = new List<(HotkeyMode, Action)>();
                list.Add((mode, action));
            }
            Log($"[Input] Hotkey VK=0x{vk:X} {mode} added");
        }
        public static void ClearHotkeys(int vk) { lock (_hk) _hotkeys.Remove(vk); }
        public static void ClearAllHotkeys() { lock (_hk) _hotkeys.Clear(); }

        // Edge event-style (named actions), mirrors your old API
        public delegate void KeyStateChangedHandler(object? s, KeyEventArgs e);
        public sealed class KeyEventArgs : EventArgs { public int KeyCode; public bool IsPressed; public KeyEventArgs(int k, bool p) { KeyCode = k; IsPressed = p; } }

        public static int RegisterKeyAction(int vk, string actionName, KeyStateChangedHandler handler)
        {
            if (!IsReady || handler == null || string.IsNullOrWhiteSpace(actionName)) return -1;
            lock (_evt)
            {
                if (!_actions.TryGetValue(vk, out var list))
                    _actions[vk] = list = new List<ActionRec>();
                var existing = list.FirstOrDefault(a => a.Name == actionName);
                if (existing != null) { existing.Handler = handler; return existing.Id; }
                var id = ++_nextActionId;
                list.Add(new ActionRec { Id = id, Name = actionName, Handler = handler });
                return id;
            }
        }
        public static bool UnregisterKeyAction(int vk, string actionName)
        {
            lock (_evt)
            {
                if (!_actions.TryGetValue(vk, out var list)) return false;
                var removed = list.RemoveAll(a => a.Name == actionName) > 0;
                if (list.Count == 0) _actions.Remove(vk);
                return removed;
            }
        }
        public static bool UnregisterKeyAction(int id)
        {
            lock (_evt)
            {
                foreach (var kv in _actions.ToArray())
                {
                    var removed = kv.Value.RemoveAll(a => a.Id == id) > 0;
                    if (kv.Value.Count == 0) _actions.Remove(kv.Key);
                    if (removed) return true;
                }
                return false;
            }
        }

        // ===== worker =====
        private static void Worker()
        {
            Log($"[Input] worker @{_pollMs}ms");
            while (_run)
            {
                try
                {
                    UpdateKeys();
                    FireHotkeys();
                }
                catch (Exception e) { Log($"[Input] tick error: {e.Message}"); }
                finally { Thread.Sleep(_pollMs); }
            }
        }

        private static unsafe void UpdateKeys()
        {
            if (!IsReady) return;

            Buffer.BlockCopy(_curr, 0, _prev, 0, 64);

            fixed (byte* p = _curr)
            {
                if (!_vmm.MemRead(_pidWinlogonKM, _gaf, (nint)p, 64, out var read,
                    VmmFlags.NOCACHE | VmmFlags.NO_PREDICTIVE_READ | VmmFlags.NOCACHEPUT) || read != 64)
                {
                    // keep last state; soft-fail
                    return;
                }
            }
            for (int i = 1; i <= 5; i++)
            {
                int vk = i switch { 1 => 0x01, 2 => 0x02, 3 => 0x04, 4 => 0x05, 5 => 0x06, _ => 0 };
                if (vk == 0) continue;
                bool ext = ExternalMouseDown(i);        // Makcu state
                if (ext) SetDown(_curr, vk, true);      // OR Makcu into our bitmap
                // (if you want Makcu to also be able to clear bits, call SetDown(_curr, vk, ext) instead)
            }
            // Edge notifications
            lock (_evt)
            {
                foreach (var kv in _actions.ToArray())
                {
                    int vk = kv.Key;
                    bool was = IsDown(_prev, vk);
                    bool now = IsDown(_curr, vk);
                    if (was == now) continue;
                    foreach (var rec in kv.Value.ToArray())
                    {
                        try { rec.Handler?.Invoke(null, new KeyEventArgs(vk, now)); }
                        catch (Exception ex) { Log($"[Input] action '{rec.Name}' err: {ex.Message}"); }
                    }
                }
            }
        }

        private static void FireHotkeys()
        {
            Dictionary<int, List<(HotkeyMode, Action)>> snap;
            lock (_hk) snap = _hotkeys.ToDictionary(k => k.Key, v => v.Value.ToList());

            foreach (var (vk, list) in snap)
            {
                bool was = IsDown(_prev, vk);
                bool now = IsDown(_curr, vk);
                foreach (var (mode, act) in list)
                {
                    try
                    {
                        if (mode == HotkeyMode.OnPress && !was && now) act();
                        if (mode == HotkeyMode.OnRelease && was && !now) act();
                        if (mode == HotkeyMode.WhileDown && now) act();
                    }
                    catch (Exception ex) { Log($"[Input] hotkey VK=0x{vk:X} err: {ex.Message}"); }
                }
            }
        }

        // ===== resolver (Win11+) =====
        private static bool ResolveNewWindows()
        {
            Log("[Input] Win11+ resolver (sig-scan) �?");
            var csrssPids = _vmm.GetPidsByName("csrss.exe") ?? Array.Empty<uint>();
            if (csrssPids.Length == 0) { Log("[Input] csrss.exe not found"); return false; }

            foreach (var pidUser in csrssPids)
            {
                var pid = KM(pidUser);  // <�? key change

                // win32k*.sys (session module lives here)
                if (!TryWin32k(pid, out var kBase, out var kSize)) continue;

                // g_session_global_slots
                var gSessPtr = _vmm.FindSignature(pid, kBase, kBase + kSize, "48 8B 05 ? ? ? ? 48 8B 04 C8");
                if (gSessPtr == 0)
                    gSessPtr = _vmm.FindSignature(pid, kBase, kBase + kSize, "48 8B 05 ? ? ? ? FF C9");
                if (gSessPtr == 0) { Log("[Input] g_session_global_slots sig not found"); continue; }

                var rel = Read<int>(pid, gSessPtr + 3);         // <�? use KM pid
                if (!rel.ok) { Log("[Input] rel read fail"); continue; }
                var gSlots = gSessPtr + 7UL + (ulong)rel.val;

                // walk slots �? user_session_state
                ulong userSession = 0;
                for (int i = 0; i < 8; i++)
                {
                    var t1 = Read<ulong>(pid, gSlots); if (!t1.ok) continue;
                    var t2 = Read<ulong>(pid, t1.val + (ulong)(8 * i)); if (!t2.ok) continue;
                    var t3 = Read<ulong>(pid, t2.val); if (!t3.ok) continue;
                    userSession = t3.val;
                    if (userSession > 0x7FFFFFFFFFFF) break;
                }
                if (userSession <= 0x7FFFFFFFFFFF) { Log("[Input] user_session_state not sane"); continue; }

                // win32kbase.sys (offset into session)
                if (!_vmm.GetModuleInfo(pid, "win32kbase.sys", out var kbase, out var ksz))
                { Log("[Input] win32kbase.sys not found"); continue; }

                var p = _vmm.FindSignature(pid, kbase, kbase + ksz, "48 8D 90 ? ? ? ? E8 ? ? ? ? 0F 57 C0");
                if (p == 0) { Log("[Input] session offset sig not found"); continue; }

                var off = Read<uint>(pid, p + 3);               // <�? use KM pid
                if (!off.ok) { Log("[Input] session offset read fail"); continue; }

                _gaf = userSession + off.val;
                if (_gaf > 0x7FFFFFFFFFFF) { Log($"[Input] gaf=0x{_gaf:X}"); return true; }
                Log("[Input] computed gaf not canonical");
                Log($"[Input] scanning csrss pid {pidUser} (KM=0x{pid:X})");
            }
            
            return false;
        }


        // ===== resolver (Win10/older) =====
        private static bool ResolveOldWindows()
        {
            Log("[Input] Win10 resolver (EAT/PDB) �?");

            // Try EAT
            var eat = _vmm.GetExportVA(_pidWinlogonKM, "win32kbase.sys", "gafAsyncKeyState");
            if (eat > 0x7FFFFFFFFFFF) { _gaf = eat; Log("[Input] gaf via EAT"); return true; }

            // Try PDB symbol
            if (_vmm.PdbSymbolAddress(_pidWinlogonKM, "win32kbase.sys", "gafAsyncKeyState", out var sym) &&
                sym > 0x7FFFFFFFFFFF)
            { _gaf = sym; Log("[Input] gaf via PDB"); return true; }

            Log("[Input] Failed to resolve gaf on Win10 path");
            return false;
        }

        private static bool TryWin32k(uint pid, out ulong baseAddr, out ulong size)
        {
            // these are kernel modules; pid must be KM(pid)
            if (_vmm.GetModuleInfo(pid, "win32ksgd.sys", out baseAddr, out size)) return true;
            if (_vmm.GetModuleInfo(pid, "win32k.sys",   out baseAddr, out size)) return true;
            baseAddr = size = 0; return false;
        }

        private static (bool ok, T val) Read<T>(uint pid, ulong va) where T : unmanaged
        {
            unsafe
            {
                T v = default;
                uint cb = (uint)Unsafe.SizeOf<T>();
                {
                    if (_vmm.MemRead(pid, va, (nint)Unsafe.AsPointer(ref v), cb, out var r, VmmFlags.NOCACHE | VmmFlags.NO_PREDICTIVE_READ) && r == cb)
                        return (true, v);
                }
                return (false, default);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDown(byte[] bmp, int vk)
        {
            int idx = (vk * 2) >> 3;
            int bit = 1 << ((vk % 4) * 2);
            return (bmp[idx] & bit) != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetDown(byte[] bmp, int vk, bool down)
        {
            int idx = (vk * 2) >> 3;
            int bit = 1 << ((vk % 4) * 2);
            if (down) bmp[idx] |= (byte)bit;
            else      bmp[idx] &= (byte)~bit;
        }
        private static void Log(string s) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {s}");

        // ===== static fields =====
        private static IVmmEx? _vmm;
        private static uint _pidWinlogon, _pidWinlogonKM;
        private static int _build; private static uint _ubr;

        private static volatile bool _safeMode;
        private static volatile bool _initialized;
        private static ulong _gaf;

        private static readonly byte[] _curr = new byte[64];
        private static readonly byte[] _prev = new byte[64];

        private static readonly object _gate = new();
        private static readonly Dictionary<int, DateTime> _lastTap = new();
        private static readonly Dictionary<int, bool> _held = new();
        private const int DoubleTapMs = 300;

        private static Thread? _thread;
        private static volatile bool _run;
        private static int _pollMs = 10; // At most 100 Hz.

        // hotkeys
        private static readonly object _hk = new();
        private static readonly Dictionary<int, List<(HotkeyMode, Action)>> _hotkeys = new();

        // edge actions
        private static readonly object _evt = new();
        private static readonly Dictionary<int, List<ActionRec>> _actions = new();
        private static int _nextActionId = 0;
        private sealed class ActionRec { public int Id; public string Name = ""; public KeyStateChangedHandler? Handler; }
    }    
}

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MystieHook.Input;
using MystieHook.Services;
using VmmSharpEx;
using VmmSharpEx.Extensions;
using VmmSharpEx.Scatter;
using VmmSharpEx.Scatter.V2;
using VFlags = VmmSharpEx.Options.VmmFlags;

namespace MystieHook;

public static class DmaMemory
{
    #region Fields / Properties

    private static Vmm? _vmm;
    // Avoid throwing from properties during probes; use guards instead.
    public static bool IsVmmReady => _vmm is not null;

    public static uint Pid { get; private set; }
    public static ulong Base { get; private set; }
    public static bool IsAttached => _vmm is not null && Pid != 0 && Base != 0;
    public static Vmm? Vmm => _vmm;

    #endregion

    #region Init / Attach / Dispose

    /// <summary>Initialize VMM only (no attach). Applies memory map if requested. Safe to call multiple times.</summary>
    public static void InitOnly(string device = "fpga", bool applyMMap = true)
    {
        if (_vmm is null)
        {
            var initialized = new Vmm(new[] { "-printf", "-v", "-device", device, "-waitinitialize" });
            try
            {
                VmmRefreshExperiment.Initialize(initialized.ConfigGet, initialized.ConfigSet);
                _vmm = initialized;
            }
            catch
            {
                initialized.Dispose();
                throw;
            }
        }

        if (applyMMap)
        {
            try { _ = _vmm.GetMemoryMap(applyMap: true, outputFile: "mmap.txt"); } catch { Logger.Warn("Failed to apply memory map.");}
        }
    }

    /// <summary>Re-apply memory map on an already initialized VMM.</summary>
    public static void RefreshMemoryMap(string output = "mmap.txt")
    {
        if (_vmm is null) return;
        try { _ = _vmm.GetMemoryMap(applyMap: true, outputFile: output); } catch { }
    }

    /// <summary>Initializes VMM (if needed) and blocks until the PID/module base are found.</summary>
    public static void Attach(string exeName, string? moduleName = null, string device = "fpga", bool applyMMap = true)
    {
        InitOnly(device, applyMMap);
        if (_vmm is null) throw new InvalidOperationException("VMM failed to initialize.");

        Pid = WaitForPidByName(exeName);
        Base = WaitForModuleBase(Pid, moduleName ?? exeName);
    }

    /// <summary>Non-blocking probe: returns true if PID & Base resolved right now.</summary>
    public static bool TryAttachOnce(string exeName, out string error, string? moduleName = null, string device = "fpga", bool applyMMap = true)
    {
        error = "";
        try
        {
            InitOnly(device, applyMMap);
            if (_vmm is null) { error = "VMM not initialized."; return false; }

            if (!_vmm.PidGetFromName(exeName, out var pid) || pid == 0) { error = "PID not found."; return false; }

            var baseAddr = _vmm.ProcessGetModuleBase(pid, moduleName ?? exeName);
            if (baseAddr == 0) { error = "Module base not found."; return false; }

            Pid = pid; Base = baseAddr;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static void Dispose()
    {
        try { _vmm?.Dispose(); } catch { }
        _vmm = null; Pid = 0; Base = 0;
    }

    // External-attach plumbing (optional shim API)
    private static Func<(bool attached, uint pid, ulong @base)> _probe = () => (false, 0, 0);
    private static Func<ulong, (bool ok, ulong val)> _readU64 = _ => (false, 0);
    private static Func<ulong, int, byte[]?> _readBytes = (_, __) => null;

    public static bool Attached => _probe().attached;

    public static void AttachExternal(
        Func<(bool attached, uint pid, ulong @base)> probe,
        Func<ulong, (bool ok, ulong val)> readU64, // wrapper pattern from DmaMemory.Read
        Func<ulong, int, byte[]?> readBytes)
    {
        _probe = probe;
        _readU64 = readU64;
        _readBytes = readBytes;
    }
    #endregion

    #region Guards

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureInitialized()
    {
        if (_vmm is null)
            throw new InvalidOperationException("VMM not initialized. Call DmaMemory.InitOnly() or Attach() first.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureAttached()
    {
        EnsureInitialized();
        if (Pid == 0 || Base == 0)
            throw new InvalidOperationException("Not attached to a process. Call DmaMemory.Attach() first.");
    }

    #endregion

    #region Process Enumeration

    public readonly record struct ProcEntry(uint Pid, string Name, bool IsWow64);

    /// <summary>Returns (Pid, Name, IsWow64) for all running processes.</summary>
    public static List<ProcEntry> GetProcessList()
    {
        EnsureInitialized();

        try
        {
            var infos = _vmm!.ProcessGetInformationAll(); // ProcessInfo[]
            if (infos is null || infos.Length == 0) return new();

            return infos
                .Select(pi => new ProcEntry(pi.dwPID, pi.sNameLong ?? string.Empty, pi.fWow64))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Pid)
                .ToList();
        }
        catch
        {
            // Fallback: enumerate PIDs and try to pull main module name.
            var list = new List<ProcEntry>();
            var pids = _vmm!.PidGetList() ?? Array.Empty<uint>();
            foreach (var pid in pids)
            {
                try
                {
                    var mods = _vmm.Map_GetModule(pid, fExtendedInfo: false);
                    var name = (mods != null && mods.Length > 0 && !string.IsNullOrEmpty(mods[0].sText))
                               ? mods[0].sText
                               : $"pid_{pid}";
                    list.Add(new ProcEntry(pid, name, false));
                }
                catch { /* skip PID on error */ }
            }
            return list
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Pid)
                .ToList();
        }
    }

    #endregion

    #region Module Enumeration

    public sealed class ModuleInfo
    {
        public string Name { get; init; } = "";
        public string FullName { get; init; } = "";
        public ulong Base { get; init; }
        public ulong Size { get; init; }
        public bool IsWow64 { get; init; }
    }

    /// <summary>List loaded modules for a PID (default = currently attached).</summary>
    public static List<ModuleInfo> GetModules(uint? pid = null, bool includeWow64Flag = false)
    {
        EnsureInitialized();
        uint p = pid ?? Pid;
        if (p == 0) return new();

        var arr = _vmm!.Map_GetModule(p, fExtendedInfo: false);
        if (arr == null || arr.Length == 0) return new();

        var list = new List<ModuleInfo>(arr.Length);
        foreach (var m in arr)
        {
            var name = string.IsNullOrWhiteSpace(m.sFullName)
                ? (m.sText ?? "")
                : Path.GetFileName(m.sFullName);

            list.Add(new ModuleInfo
            {
                Name = name ?? "",
                FullName = m.sFullName ?? m.sText ?? "",
                Base = m.vaBase,
                Size = m.cbImageSize,
                IsWow64 = m.fWow64
            });
        }

        return list
            .OrderBy(mi => mi.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mi => mi.Base)
            .ToList();
    }

    #endregion

    #region Read / Write Helpers

    public static bool Read<T>(ulong address, out T value) where T : unmanaged
    {
        EnsureAttached();
        return _vmm!.MemReadValue(Pid, address, out value);
    }

    public static ulong FindSignature(string signature, ulong AddrMin = 0, ulong AddrMax = ulong.MaxValue)
    {
        EnsureAttached();
        return _vmm.FindSignature(Pid, signature, AddrMin, AddrMax);
    }

    public static bool Read(ulong address, Span<byte> dst, VmmFlags flags = VmmFlags.NONE)
    {
        EnsureAttached();
        if (dst.Length == 0) return true;
        unsafe
        {
            fixed (byte* p = dst)
            {
                return _vmm!.MemRead(Pid, address, (nint)p, (uint)dst.Length, out var read, (VFlags)flags) && read == (uint)dst.Length;
            }
        }
    }
    public static T Read<T>(ulong address) where T : unmanaged
    {
        EnsureAttached();
        _vmm!.MemReadValue(Pid, address, out T v);
        return v;
    }

    public static T[]? ReadArray<T>(ulong address, int count) where T : unmanaged
    {
        EnsureAttached();
        return _vmm!.MemReadArray<T>(Pid, address, count)?.ToArray();
    }

    /// <summary>Read raw bytes. Returns null on failure. If fewer bytes were read than requested, the array is resized.</summary>
    public static byte[]? ReadBytes(ulong address, uint size, VmmFlags flags = VmmFlags.NONE)
    {
        EnsureAttached();
        if (size == 0) return Array.Empty<byte>();
        var buf = new byte[size];
        unsafe
        {
            fixed (byte* p = buf)
            {
                if (!_vmm!.MemRead(Pid, address, (nint)p, size, out var read, (VFlags)flags) || read == 0)
                    return null;
                if (read < size) Array.Resize(ref buf, (int)read);
            }
        }
        return buf;
    }
    public static string ReadObjectName(ulong obj)
    {
        try
        {
            ulong namePtr = DmaMemory.Read<ulong>(obj + 0x10);
            if (namePtr == 0) return "";
            return ReadUnicodeString(namePtr, 64);
        }
        catch
        {
            return "";
        }
    }
    public static string? ReadString(ulong address, int bytes, Encoding enc)
    {
        EnsureAttached();
        return _vmm!.MemReadString(Pid, address, bytes, enc);
    }
    public static string ReadUnicodeString(ulong address, uint maxLength = 128)
    {
        if (address == 0) return "";
        try
        {
            byte[] bytes = DmaMemory.ReadBytes(address, maxLength * 2); // 2 bytes per char
            int zeroIndex = Array.FindIndex(bytes, 0, b => b == 0);
            if (zeroIndex <= 0) zeroIndex = bytes.Length;
            return System.Text.Encoding.Unicode.GetString(bytes, 0, zeroIndex);
        }
        catch
        {
            return "";
        }
    }
    public static string ReadAsciiZ(ulong address, int max = 256, VmmFlags flags = VmmFlags.NONE)
    {
        if (max <= 0) max = 1;
        var bytes = ReadBytes(address, (uint)max, flags);
        if (bytes is null || bytes.Length == 0) return "";
        int zero = Array.IndexOf(bytes, (byte)0);
        if (zero >= 0) return Encoding.ASCII.GetString(bytes, 0, zero);
        return Encoding.ASCII.GetString(bytes);
    }

    public static string ReadUtf16Z(ulong address, int maxChars = 256, VmmFlags flags = VmmFlags.NONE)
    {
        if (maxChars <= 0) maxChars = 1;
        var bytes = ReadBytes(address, (uint)(maxChars * 2), flags);
        if (bytes is null || bytes.Length == 0) return "";
        int end = -1;
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0) { end = i; break; }
        }
        if (end < 0) end = bytes.Length - (bytes.Length % 2);
        return Encoding.Unicode.GetString(bytes, 0, end);
    }

    public static string? ReadUnicodeZ(ulong address, int max = 256)
    {
        EnsureAttached();
        return _vmm!.MemReadString(Pid, address, max * 2, Encoding.Unicode);
    }

    public static string? ReadUtf32Z(ulong address, int max = 256)
    {
        EnsureAttached();
        return _vmm!.MemReadString(Pid, address, max * 2, Encoding.UTF32);
    }

    public static string? ReadBigUnicodeZ(ulong address, int max = 256)
    {
        EnsureAttached();
        return _vmm!.MemReadString(Pid, address, max * 2, Encoding.BigEndianUnicode);
    }

    public static bool Write<T>(ulong address, in T value) where T : unmanaged
    {
        EnsureAttached();
        return _vmm!.MemWriteValue(Pid, address, value);
    }

    #endregion

    #region Scatter Helpers

    /// <summary>Create a new scatter map bound to the current game PID.</summary>
    public static ScatterReadMap Scatter()
    {
        EnsureAttached();
        return new ScatterReadMap(_vmm!, Pid);
    }

    /// <summary>
    /// Execute a single scatter round defined by <paramref name="define"/>.
    /// </summary>
    public static void ScatterRound(Action<ScatterReadRound> define, bool useCache = false)
    {
        EnsureAttached();

        using var map = new ScatterReadMap(_vmm!, Pid);
        var rd = map.AddRound(useCache);
        define(rd);
        map.Execute();
    }

    /// <summary>
    /// Follow a pointer chain with direct reads: result = (((base + o0)-> + o1)-> + ...).
    /// Returns false if any hop fails or resolves to 0.
    /// </summary>
    public static bool TryFollow(ulong baseAddr, ReadOnlySpan<ulong> offsets, out ulong result)
    {
        result = baseAddr;
        foreach (var off in offsets)
        {
            ulong nextAddr = result + off;        // add offset
            if (!Read(nextAddr, out result) || result == 0) // deref
                return false;
        }
        return true;
    }

    #endregion

    #region Private Wait Helpers

    private static uint WaitForPidByName(string exe)
    {
        EnsureInitialized();
        while (true)
        {
            if (_vmm!.PidGetFromName(exe, out var pid) && pid != 0) return pid;
            Logger.Info($"Waiting for process '{exe}'...");
            Thread.Sleep(300);
        }
    }

    private static ulong WaitForModuleBase(uint pid, string module)
    {
        EnsureInitialized();
        while (true)
        {
            var b = _vmm!.ProcessGetModuleBase(pid, module);
            if (b != 0) return b;
            Logger.Info($"Waiting for module '{module}' in PID {pid}...");
            Thread.Sleep(300);
        }
    }

    #endregion

    #region IVmmEx Adapter + RTTI helpers

    public sealed class VmmSharpExAdapter : IVmmEx
    {
        private readonly Vmm _vmm;
        public VmmSharpExAdapter(Vmm vmm) => _vmm = vmm ?? throw new ArgumentNullException(nameof(vmm));

        #region Registry

        public string RegReadString(string path)
        {
            var bytes = _vmm.WinReg_QueryValue(path, out uint type);
            if (bytes == null || bytes.Length == 0) return string.Empty;

            if (type == 1 || type == 2) // REG_SZ / REG_EXPAND_SZ
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');

            if (type == 7 || type == 3) // REG_MULTI_SZ / REG_BINARY (lenient)
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');

            return string.Empty;
        }

        public uint RegReadDword(string path)
        {
            var bytes = _vmm.WinReg_QueryValue(path, out uint type);
            if (bytes == null || bytes.Length == 0) return 0;

            if (type == 4 && bytes.Length >= 4) // REG_DWORD
                return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));

            if (type == 1 || type == 2)
            {
                var s = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                return uint.TryParse(s, out var v) ? v : 0;
            }
            return 0;
        }

        #endregion

        #region Process / Module Info

        public uint GetPidByName(string name)
            => _vmm.PidGetFromName(name, out var pid) && pid != 0 ? pid : 0;

        public uint[] GetPidsByName(string name)
        {
            var pids = _vmm.PidGetList();
            if (pids == null || pids.Length == 0) return Array.Empty<uint>();

            var mod1 = name;
            var mod2 = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : (name + ".exe");

            return pids.Where(pid =>
            {
                var b1 = _vmm.ProcessGetModuleBase(pid, mod1);
                if (b1 != 0) return true;
                var b2 = _vmm.ProcessGetModuleBase(pid, mod2);
                return b2 != 0;
            }).ToArray();
        }

        public bool GetModuleInfo(uint pid, string moduleName, out ulong baseAddress, out ulong imageSize)
        {
            baseAddress = _vmm.ProcessGetModuleBase(pid, moduleName);
            if (baseAddress == 0) { imageSize = 0; return false; }

            Span<byte> hdr = stackalloc byte[0x1000];
            unsafe
            {
                fixed (byte* p = hdr)
                {
                    if (!_vmm.MemRead(pid, baseAddress, (nint)p, (uint)hdr.Length, out var read, VFlags.NOCACHE) || read < 0x400)
                    {
                        imageSize = GuessSize(moduleName);
                        return true;
                    }
                }
            }

            if (hdr[0] != (byte)'M' || hdr[1] != (byte)'Z')
            {
                imageSize = GuessSize(moduleName);
                return true;
            }

            int e_lfanew = BinaryPrimitives.ReadInt32LittleEndian(hdr.Slice(0x3C, 4));
            if (e_lfanew <= 0 || e_lfanew + 0x100 > hdr.Length) { imageSize = GuessSize(moduleName); return true; }

            if (hdr[e_lfanew] != (byte)'P' || hdr[e_lfanew + 1] != (byte)'E')
            { imageSize = GuessSize(moduleName); return true; }

            int optOff = e_lfanew + 24;
            if (optOff + 0x38 + 4 > hdr.Length) { imageSize = GuessSize(moduleName); return true; }

            uint sz = BinaryPrimitives.ReadUInt32LittleEndian(hdr.Slice(optOff + 0x38, 4));
            imageSize = sz != 0 ? sz : GuessSize(moduleName);
            return true;
        }

        private static ulong GuessSize(string module)
            => module.Contains("win32k", StringComparison.OrdinalIgnoreCase) ? 0x800000UL : 0x400000UL;

        #endregion

        #region Memory / Scan / Symbols

        public unsafe bool MemRead(uint pid, ulong va, nint pb, uint cb, out uint cbRead, VmmFlags flags)
            => _vmm.MemRead(pid, va, pb, cb, out cbRead, (VFlags)flags);

        public ulong FindSignature(uint pid, ulong start, ulong end, string idaStylePattern)
        {
            if (end <= start) return 0;

            var (needle, mask) = ParsePattern(idaStylePattern);
            if (needle.Length == 0) return 0;

            const int chunk = 256 * 1024;
            int overlap = Math.Max(needle.Length - 1, 0);

            byte[] buf = new byte[chunk + overlap];

            ulong cur = start;
            int valid = 0;

            while (cur < end)
            {
                int toRead = (int)Math.Min((ulong)chunk, end - cur);
                unsafe
                {
                    fixed (byte* p = buf)
                    {
                        if (!_vmm.MemRead(pid, cur, (nint)p, (uint)toRead, out var read, VFlags.NOCACHE | VFlags.NO_PREDICTIVE_READ))
                            return 0;
                        valid = (int)read;
                    }
                }
                if (valid <= 0) return 0;

                int limit = valid - needle.Length + 1;
                for (int i = 0; i < limit; i++)
                {
                    if (Match(buf, i, needle, mask))
                        return cur + (uint)i;
                }

                cur += (uint)limit;
                if (cur >= end) break;

                Buffer.BlockCopy(buf, valid - overlap, buf, 0, overlap);
            }

            return 0;
        }

        private static (byte[] bytes, bool[] mask) ParsePattern(string pattern)
        {
            var parts = pattern.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>(parts.Length);
            var mask = new List<bool>(parts.Length);

            foreach (var p in parts)
            {
                if (p == "?" || p == "??") { bytes.Add(0); mask.Add(false); continue; }
                if (byte.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                { bytes.Add(b); mask.Add(true); }
            }
            return (bytes.ToArray(), mask.ToArray());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Match(byte[] hay, int offset, byte[] needle, bool[] mask)
        {
            for (int i = 0; i < needle.Length; i++)
                if (mask[i] && hay[offset + i] != needle[i]) return false;
            return true;
        }

        public ulong GetExportVA(uint pid, string moduleName, string function)
        {
            var kernelPid = pid | IVmmEx.PID_PROCESS_WITH_KERNELMEMORY;
            var entries = _vmm.Map_GetEAT(kernelPid, moduleName, out var eatInfo);
            if (entries == null || entries.Length == 0 || !eatInfo.fValid)
                return 0;

            foreach (var e in entries)
            {
                if (string.Equals(e.sFunction, function, StringComparison.Ordinal))
                    return e.vaFunction;
            }
            return 0;
        }

        public bool PdbSymbolAddress(uint pid, string moduleName, string symbol, out ulong va)
        {
            _ = _vmm.ProcessGetModuleBase(pid, moduleName);
            var ok = _vmm.PdbSymbolAddress(moduleName, symbol, out va);
            return ok && va != 0;
        }

        #endregion
    }

    public static class Rtti
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CompleteObjectLocator
        {
            public uint signature, offset, cdOffset, typeDescriptorRva, classDescriptorRva, objectBase;
        }

        // Cache module ranges once
        private static (ulong Base, ulong End)[] _mods = Array.Empty<(ulong, ulong)>();
        private static IVmmEx? _vmm;

        public static void BuildModuleRanges(params string[] modules)
        {
            // Lazy bind the adapter to current VMM on first use
            if (_vmm is null)
            {
                if (!IsVmmReady) throw new InvalidOperationException("VMM not initialized.");
                _vmm = new VmmSharpExAdapter(DmaMemory._vmm!);
            }

            var list = new List<(ulong, ulong)>();
            foreach (var m in modules)
                if (_vmm.GetModuleInfo(DmaMemory.Pid, m, out var b, out var sz) && b != 0 && sz != 0)
                    list.Add((b, b + sz));
            _mods = list.ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool InAnyModule(ulong va)
        {
            foreach (var (b, e) in _mods)
                if (va >= b && va < e) return true;
            return false;
        }

        public static bool TryRead(ulong obj, out string name)
        {
            name = "";
            if (!DmaMemory.Read(obj, out ulong vtable) || (vtable & 0x7) != 0 || !InAnyModule(vtable)) return false;
            if (!DmaMemory.Read(vtable - 8, out ulong colPtr) || colPtr == 0 || !InAnyModule(colPtr)) return false;

            if (!DmaMemory.Read(colPtr, out CompleteObjectLocator col) || col.signature != 1) return false;

            ulong imageBase = colPtr - col.objectBase;
            ulong typeDescPtr = imageBase + col.typeDescriptorRva;
            if (!InAnyModule(typeDescPtr)) return false;

            var s = DmaMemory.ReadString(typeDescPtr + 0x14, 128, Encoding.ASCII);
            if (string.IsNullOrEmpty(s)) return false;

            if (s.StartsWith(".?AV")) s = s[4..];
            int cut;
            if ((cut = s.IndexOf("@@", StringComparison.Ordinal)) >= 0) s = s[..cut];
            foreach (var suf in new[] { "@gamecode", "@enf", "@gamelib" })
                if ((cut = s.IndexOf(suf, StringComparison.Ordinal)) >= 0) s = s[..cut];

            name = s;
            return !string.IsNullOrWhiteSpace(name);
        }

        public static string ReadRtti(ulong addr)
        {
            if (!DmaMemory.Read(addr, out ulong vtable) || vtable == 0) return "Invalid vtable";
            if (!DmaMemory.Read(vtable - 8, out ulong rttiPtr) || rttiPtr == 0) return "Invalid RTTI pointer";

            CompleteObjectLocator col;
            if (!DmaMemory.Read(rttiPtr, out col)) return "Bad COL read";
            if (col.signature != 1) return $"Bad signature: {col.signature}";

            ulong imageBase = rttiPtr - col.objectBase;
            ulong typeDescPtr = imageBase + col.typeDescriptorRva;

            string? name = DmaMemory.ReadString(typeDescPtr + 0x14, 128, Encoding.ASCII);
            if (string.IsNullOrEmpty(name)) return "<?>";
            if (name.StartsWith(".?AV")) name = name.Substring(4);
            if (name.EndsWith("@@")) name = name.Substring(0, name.IndexOf("@@", StringComparison.Ordinal));
            foreach (var suf in new[] { "@gamecode", "@enf", "@gamelib" })
                if (name.EndsWith(suf, StringComparison.Ordinal))
                    name = name.Substring(0, name.IndexOf(suf, StringComparison.Ordinal));

            return name;
        }
    }

    #endregion

    #region Prefab path helpers

    private static readonly ConcurrentDictionary<ulong, ulong> _ptrCache = new();

    public static bool TryGetPath(ulong prefabDataClass, out string path)
    {
        path = null!;
        if (prefabDataClass == 0) return false;

        if (_ptrCache.TryGetValue(prefabDataClass, out var cached) && cached != 0)
            return TryReadAsciiZ(cached, out path);

        // Probe first ~0x180 bytes of the class for plausible char* fields
        const int scanBytes = 0x180;
        if (!ReadBytesSlow(prefabDataClass, scanBytes, out var buf)) return false;

        for (int off = 0; off + 8 <= buf.Length; off += 8)
        {
            ulong p = BitConverter.ToUInt64(buf, off);
            if (p < 0x10000UL) continue;               // junk
            if (!TryReadAsciiZ(p, out var s)) continue;
            if (s.IndexOf(".ent", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf('/', StringComparison.Ordinal) >= 0)
            {
                _ptrCache[prefabDataClass] = p;
                path = s;
                return true;
            }
        }

        _ptrCache[prefabDataClass] = 0;
        return false;
    }

    // renamed to avoid colliding with public ReadBytes()
    private static bool ReadBytesSlow(ulong addr, int len, out byte[] buf)
    {
        buf = new byte[len];
        for (int i = 0; i < len; i++)
        {
            if (!Read(addr + (ulong)i, out buf[i])) { buf = null!; return false; }
        }
        return true;
    }

    private static bool TryReadAsciiZ(ulong addr, out string s)
    {
        Span<byte> tmp = stackalloc byte[160];
        int n = 0;
        for (; n < tmp.Length; n++)
        {
            if (!Read(addr + (ulong)n, out byte b)) { s = null!; return false; }
            if (b == 0) break;
            if (b < 32 || b >= 127) { s = null!; return false; }
            tmp[n] = b;
        }
        s = (n > 0) ? Encoding.ASCII.GetString(tmp[..n].ToArray()) : null!;
        return !string.IsNullOrEmpty(s);
    }

    #endregion
}

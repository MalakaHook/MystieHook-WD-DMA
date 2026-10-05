using System;
using System.Threading.Tasks;

namespace MystieHook.Services
{
    public static class VmmService
    {
        public static void InitOnly()
        {
            JobSystem.Schedule(() =>
            {
                try
                {
                    MystieHook.DmaMemory.InitOnly("fpga", applyMMap: true);
                    Snapshots.Mutate(s => s with { Status = "VMM ready", VmmReady = true });
                }
                catch (Exception ex)
                {
                    Snapshots.Mutate(s => s with { Status = "VMM init failed: " + ex.Message, VmmReady = false });
                }
            });
        }

        public static void Attach(string exe)
        {
            JobSystem.Schedule(() =>
            {
                try
                {
                    if (MystieHook.DmaMemory.TryAttachOnce(exe, out var err))
                    {
                        Snapshots.Mutate(s => s with
                        {
                            Status = $"Attached PID={MystieHook.DmaMemory.Pid} base=0x{MystieHook.DmaMemory.Base:X}",
                            VmmReady = true,
                            Pid = (int)MystieHook.DmaMemory.Pid,
                            MainBase = MystieHook.DmaMemory.Base
                        });
                    }
                    else
                    {
                        Snapshots.Mutate(s => s with { Status = "Attach failed: " + err });
                    }
                }
                catch (Exception ex)
                {
                    Snapshots.Mutate(s => s with { Status = "Attach error: " + ex.Message });
                }
            });
        }

        public static void RefreshProcesses()
        {
            JobSystem.Schedule(() =>
            {
                try
                {
                    var list = MystieHook.DmaMemory.GetProcessList(); // List<ProcEntry>
                    Snapshots.Mutate(s => s with
                    {
                        Processes = (list != null) ? list.ToArray()
                                                   : Array.Empty<MystieHook.DmaMemory.ProcEntry>()
                    });
                }
                catch (Exception ex)
                {
                    Snapshots.Mutate(s => s with { Status = "Proc list error: " + ex.Message });
                }
            });
        }

        public static void RefreshModules()
        {
            JobSystem.Schedule(() =>
            {
                if (!MystieHook.DmaMemory.IsVmmReady)
                {
                    // Optional: only set status once or throttle
                    Snapshots.Mutate(s => s with { Status = "Init VMM first to enumerate processes." });
                    return;
                }
                try
                {
                    var mods = MystieHook.DmaMemory.GetModules(); // List<ModuleInfo>
                    Snapshots.Mutate(s => s with
                    {
                        Modules = (mods != null) ? mods.ToArray()
                                                 : Array.Empty<MystieHook.DmaMemory.ModuleInfo>()
                    });
                }
                catch (Exception ex)
                {
                    Snapshots.Mutate(s => s with { Status = "Modules error: " + ex.Message });
                }
            });
        }

        /// <summary>Dispose VMM + clear related state (safe to call repeatedly).</summary>
        public static void DisposeVmm()
        {
            JobSystem.Schedule(() =>
            {
                if (!MystieHook.DmaMemory.IsVmmReady)
                {
                    // Optional: only set status once or throttle
                    Snapshots.Mutate(s => s with { Status = "Init VMM first to enumerate processes." });
                    return;
                }
                try
                {
                    MystieHook.DmaMemory.Dispose();
                    Snapshots.Mutate(s => s with
                    {
                        Status = "Disposed.",
                        VmmReady = false,
                        Processes = Array.Empty<MystieHook.DmaMemory.ProcEntry>(),
                        Modules   = Array.Empty<MystieHook.DmaMemory.ModuleInfo>(),
                        Pid = 0,
                        MainBase = 0
                    });
                }
                catch (Exception ex)
                {
                    Snapshots.Mutate(s => s with { Status = "Dispose error: " + ex.Message });
                }
            });
        }
    }
}

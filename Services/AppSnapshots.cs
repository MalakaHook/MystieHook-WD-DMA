using System.Threading;

namespace MystieHook.Services;

// Public, immutable snapshot of app state
public record AppSnapshot(
    string Status,
    bool VmmReady,
    DmaMemory.ProcEntry[] Processes,
    DmaMemory.ModuleInfo[] Modules,
    int Pid,
    ulong MainBase
)
{
    public static readonly AppSnapshot Empty = new(
        Status: "Idle.",
        VmmReady: false,
        Processes: System.Array.Empty<DmaMemory.ProcEntry>(),
        Modules: System.Array.Empty<DmaMemory.ModuleInfo>(),
        Pid: 0,
        MainBase: 0
    );
};

using VmmSharpEx.Options;

namespace MystieHook.Services;

public sealed record VmmRefreshTiming(ulong? Enabled, ulong? TickMilliseconds,
    ulong? MemoryTicks, ulong? TranslationTicks, ulong? PartialProcessTicks, ulong? TotalProcessTicks);
public sealed record VmmRefreshResult(bool Requested, string Status,
    VmmRefreshTiming Before, VmmRefreshTiming After);

// Startup-only diagnostic experiment. Never called by snapshot or rendering workers.
public static class VmmRefreshExperiment
{
    public static VmmRefreshResult Current { get; private set; }

    public static void Initialize(Func<VmmOption, ulong?> get, Func<VmmOption, ulong, bool> set)
    {
        Current = null;
        VmmRefreshTiming Read() => new(get(VmmOption.CONFIG_IS_REFRESH_ENABLED),get(VmmOption.CONFIG_TICK_PERIOD),
            get(VmmOption.CONFIG_READCACHE_TICKS),get(VmmOption.CONFIG_TLBCACHE_TICKS),
            get(VmmOption.CONFIG_PROCCACHE_TICKS_PARTIAL),get(VmmOption.CONFIG_PROCCACHE_TICKS_TOTAL));
        var before=Read();
        // Matches ConfigureDmaPerformance in the supplied reference (not a true off switch).
        if (!set(VmmOption.CONFIG_PROCCACHE_TICKS_PARTIAL,1000000) || !set(VmmOption.CONFIG_PROCCACHE_TICKS_TOTAL,1000000))
            throw new InvalidOperationException("Reference process-refresh settings could not be applied.");
        var after=Read();
        if (after != before with { PartialProcessTicks=1000000,TotalProcessTicks=1000000 })
            throw new InvalidOperationException("Reference process-refresh readback failed.");
        Current=new(true,"ProcessRefresh1000000Ticks",before,after);
        Logger.Info($"VMM refresh: {Current.Status}; before={Current.Before}; after={Current.After}");
    }

    public static VmmRefreshResult Apply(bool requested, Func<VmmOption, ulong?> get,
        Func<VmmOption, ulong, bool> set, bool tlbOneSecond = false)
    {
        VmmRefreshTiming Read() => new(get(VmmOption.CONFIG_IS_REFRESH_ENABLED),
            get(VmmOption.CONFIG_TICK_PERIOD), get(VmmOption.CONFIG_READCACHE_TICKS),
            get(VmmOption.CONFIG_TLBCACHE_TICKS), get(VmmOption.CONFIG_PROCCACHE_TICKS_PARTIAL),
            get(VmmOption.CONFIG_PROCCACHE_TICKS_TOTAL));
        var before = Read();
        if (tlbOneSecond)
        {
            // Require an exact one-second interval and a known longer starting interval.
            // Do not combine this with the process-refresh experiment.
            if (requested || before.Enabled != 1 || before.TickMilliseconds is null or 0 or > 1000 ||
                1000 % before.TickMilliseconds.Value != 0 || before.TranslationTicks is null ||
                before.TranslationTicks <= 1000 / before.TickMilliseconds.Value)
                return new(true, "Unsupported", before, before);
            var ticks = 1000 / before.TickMilliseconds.Value;
            if (!set(VmmOption.CONFIG_TLBCACHE_TICKS, ticks))
                throw new InvalidOperationException("VMM TLB experiment could not be applied. Restart with the normal launcher.");
            var verified = Read();
            if (verified != before with { TranslationTicks = ticks })
                throw new InvalidOperationException("VMM TLB experiment verification failed. Restart with the normal launcher.");
            return new(true, "TlbIntervalOneSecond", before, verified);
        }
        if (!requested) return new(false, "Baseline", before, before);
        if (before.Enabled != 1 || before.TickMilliseconds is null or 0 ||
            before.PartialProcessTicks is null or 0 or > uint.MaxValue / 2 ||
            before.TotalProcessTicks is null or 0 or > uint.MaxValue / 2)
            return new(true, "Unsupported", before, before);

        // Failure aborts this VMM instance: no half-applied experiment may attach.
        if (!set(VmmOption.CONFIG_PROCCACHE_TICKS_PARTIAL, before.PartialProcessTicks.Value * 2) ||
            !set(VmmOption.CONFIG_PROCCACHE_TICKS_TOTAL, before.TotalProcessTicks.Value * 2))
            throw new InvalidOperationException("VMM refresh experiment could not be applied. Restart with the normal launcher.");
        var after = Read();
        if (after != before with { PartialProcessTicks = before.PartialProcessTicks * 2,
                                  TotalProcessTicks = before.TotalProcessTicks * 2 })
            throw new InvalidOperationException("VMM refresh experiment verification failed. Restart with the normal launcher.");
        return new(true, "ProcessIntervalsDoubled", before, after);
    }
}

